"""
Retrieval-augmented knowledge for the Supervisor.

Two separate knowledge bases (never mixed):
  medical  - blood donation / transfusion guidance summarised from WHO, NBTS Sri Lanka and similar sources.
             Every document must declare its publisher and source URL; documents without them are refused.
  platform - how LifeLink itself works (workflows, pages, rules).

Chunks are embedded with the Gemini embedding model and stored in two ChromaDB collections. Without a Gemini key
(or if a vector query fails) retrieval falls back to keyword scoring over the same chunks, so answers stay grounded.
"""
import hashlib
import logging
import math
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from config.settings import settings

logger = logging.getLogger("Supervisor.Knowledge")

DOMAINS = {"medical": "lifelink_medical", "platform": "lifelink_platform"}
CHUNK_SIZE = 900
CHUNK_OVERLAP = 120
_STOPWORDS = set("a an and are as at be by can do does for from how i if in is it its me my of on or should the this to was what when where which who why will with you your".split())


@dataclass
class Chunk:
    id: str
    domain: str
    text: str
    title: str
    section: str
    publisher: Optional[str] = None
    url: Optional[str] = None
    year: Optional[str] = None
    tokens: Dict[str, int] = field(default_factory=dict, repr=False)

    def source(self) -> Dict[str, Optional[str]]:
        return {"title": self.title if not self.section else f"{self.title} - {self.section}", "publisher": self.publisher, "url": self.url}


def _parse_front_matter(text: str) -> Tuple[Dict[str, str], str]:
    meta: Dict[str, str] = {}
    if text.startswith("---"):
        end = text.find("\n---", 3)
        if end != -1:
            for line in text[3:end].strip().splitlines():
                if ":" in line:
                    key, value = line.split(":", 1)
                    meta[key.strip().lower()] = value.strip().strip('"')
            text = text[end + 4:]
    return meta, text.strip()


def _tokenize(text: str) -> Dict[str, int]:
    counts: Dict[str, int] = {}
    for word in re.findall(r"[a-z0-9+\-]+", text.lower()):
        if word in _STOPWORDS or len(word) < 2:
            continue
        counts[word] = counts.get(word, 0) + 1
    return counts


def _split(body: str) -> List[Tuple[str, str]]:
    """Header-aware split: returns (section heading, text) pieces of about CHUNK_SIZE characters."""
    pieces: List[Tuple[str, str]] = []
    sections = re.split(r"\n(?=#{1,3} )", "\n" + body)
    for section in sections:
        section = section.strip()
        if not section:
            continue
        heading = ""
        if section.startswith("#"):
            first_line, _, rest = section.partition("\n")
            heading = first_line.lstrip("#").strip()
            section_text = rest.strip()
        else:
            section_text = section
        if not section_text:
            continue
        start = 0
        while start < len(section_text):
            end = min(len(section_text), start + CHUNK_SIZE)
            if end < len(section_text):
                cut = section_text.rfind("\n", start + CHUNK_SIZE // 2, end)
                end = cut if cut != -1 else end
            pieces.append((heading, section_text[start:end].strip()))
            if end >= len(section_text):
                break
            start = max(end - CHUNK_OVERLAP, start + 1)
    return pieces


def load_chunks(domain: str, directory: Optional[Path] = None) -> List[Chunk]:
    folder = (directory or settings.KNOWLEDGE_DIR) / domain
    chunks: List[Chunk] = []
    if not folder.exists():
        return chunks
    for path in sorted(folder.glob("*.md")):
        meta, body = _parse_front_matter(path.read_text(encoding="utf-8"))
        title = meta.get("title") or path.stem.replace("-", " ").title()
        if domain == "medical" and not (meta.get("publisher") and meta.get("source_url")):
            logger.warning("Skipping medical document %s: publisher and source_url are required.", path.name)
            continue
        for index, (heading, text) in enumerate(_split(body)):
            digest = hashlib.sha1(f"{path.name}|{index}|{text}".encode("utf-8")).hexdigest()[:16]
            chunks.append(Chunk(
                id=f"{domain}-{path.stem}-{index}-{digest}", domain=domain, text=text, title=title, section=heading,
                publisher=meta.get("publisher"), url=meta.get("source_url"), year=meta.get("year"),
                tokens=_tokenize(f"{title} {heading} {text}")))
    return chunks


class KnowledgeStore:
    def __init__(self, directory: Optional[Path] = None, chroma_dir: Optional[Path] = None):
        self.directory = directory or settings.KNOWLEDGE_DIR
        self.chroma_dir = chroma_dir or settings.CHROMA_DIR
        self._chunks: Dict[str, List[Chunk]] = {}
        self._client = None
        self._embedder = None
        self.vector_ready: Dict[str, bool] = {d: False for d in DOMAINS}

    def chunks(self, domain: str) -> List[Chunk]:
        if domain not in self._chunks:
            self._chunks[domain] = load_chunks(domain, self.directory)
        return self._chunks[domain]

    def _embeddings(self):
        if self._embedder is None and settings.gemini_key:
            from langchain_google_genai import GoogleGenerativeAIEmbeddings
            self._embedder = GoogleGenerativeAIEmbeddings(model=settings.EMBEDDING_MODEL, google_api_key=settings.gemini_key)
        return self._embedder

    def _collection(self, domain: str):
        if self._client is None:
            import chromadb
            self._client = chromadb.PersistentClient(path=str(self.chroma_dir))
        return self._client.get_or_create_collection(DOMAINS[domain], metadata={"hnsw:space": "cosine"})

    def ingest(self, rebuild: bool = False) -> Dict[str, Dict[str, int]]:
        """Embeds new or changed chunks into ChromaDB and removes chunks whose source text changed."""
        report: Dict[str, Dict[str, int]] = {}
        embedder = self._embeddings()
        if embedder is None:
            logger.info("No Gemini key: knowledge search uses keyword retrieval only.")
            return {d: {"chunks": len(self.chunks(d)), "embedded": 0} for d in DOMAINS}

        for domain in DOMAINS:
            self._chunks.pop(domain, None)
            chunks = self.chunks(domain)
            collection = self._collection(domain)
            existing = set(collection.get(include=[])["ids"]) if not rebuild else set()
            if rebuild and existing is not None:
                all_ids = collection.get(include=[])["ids"]
                if all_ids:
                    collection.delete(ids=all_ids)
            wanted = {c.id for c in chunks}
            stale = [i for i in existing if i not in wanted]
            if stale:
                collection.delete(ids=stale)
            new_chunks = [c for c in chunks if c.id not in existing]
            if new_chunks:
                vectors = embedder.embed_documents([c.text for c in new_chunks])
                collection.upsert(
                    ids=[c.id for c in new_chunks],
                    embeddings=vectors,
                    documents=[c.text for c in new_chunks],
                    metadatas=[{"title": c.title, "section": c.section, "publisher": c.publisher or "", "url": c.url or ""} for c in new_chunks])
            self.vector_ready[domain] = collection.count() > 0
            report[domain] = {"chunks": len(chunks), "embedded": len(new_chunks), "removed": len(stale)}
        return report

    def search(self, domain: str, query: str, k: Optional[int] = None) -> List[Chunk]:
        k = k or settings.RETRIEVAL_TOP_K
        chunks = self.chunks(domain)
        if not chunks or not query.strip():
            return []

        if self.vector_ready.get(domain):
            try:
                vector = self._embeddings().embed_query(query)
                result = self._collection(domain).query(query_embeddings=[vector], n_results=min(k, len(chunks)))
                by_id = {c.id: c for c in chunks}
                # Nothing close enough means the knowledge base does not cover the question; keyword matching
                # would only surface loosely related passages, so it is not used as a second chance here.
                return [by_id[chunk_id] for chunk_id, distance in zip(result["ids"][0], result["distances"][0])
                        if chunk_id in by_id and distance <= settings.RETRIEVAL_MAX_DISTANCE]
            except Exception as ex:
                logger.warning("Vector search failed for %s (%s); using keyword retrieval.", domain, type(ex).__name__)

        return self._keyword_search(chunks, query, k)

    @staticmethod
    def _keyword_search(chunks: List[Chunk], query: str, k: int) -> List[Chunk]:
        terms = _tokenize(query)
        if not terms:
            return []
        n = len(chunks)
        scored = []
        for chunk in chunks:
            score = 0.0
            for term in terms:
                tf = chunk.tokens.get(term, 0)
                if tf:
                    df = sum(1 for c in chunks if term in c.tokens)
                    score += (1 + math.log(tf)) * math.log(1 + n / df)
            if score > 0:
                scored.append((score, chunk))
        scored.sort(key=lambda pair: pair[0], reverse=True)
        return [chunk for _, chunk in scored[:k]]

    def status(self) -> Dict[str, Dict[str, object]]:
        return {d: {"chunks": len(self.chunks(d)), "vector": self.vector_ready.get(d, False)} for d in DOMAINS}


knowledge_store = KnowledgeStore()
