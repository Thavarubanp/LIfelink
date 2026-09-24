from knowledge.store import KnowledgeStore, load_chunks


def test_every_medical_document_carries_its_source():
    chunks = load_chunks("medical")
    assert chunks
    assert all(c.publisher and c.url and c.url.startswith("https://") for c in chunks)


def test_medical_documents_without_a_source_are_refused(tmp_path):
    (tmp_path / "medical").mkdir()
    (tmp_path / "medical" / "unsourced.md").write_text("---\ntitle: No source\n---\n# Claim\nUnverified text.", encoding="utf-8")
    (tmp_path / "platform").mkdir()
    (tmp_path / "platform" / "guide.md").write_text("---\ntitle: Guide\n---\n# Page\nHow LifeLink works.", encoding="utf-8")
    assert load_chunks("medical", tmp_path) == []
    assert len(load_chunks("platform", tmp_path)) == 1


def test_domains_are_searched_separately():
    store = KnowledgeStore()
    medical = store.search("medical", "tattoo piercing deferral")
    platform = store.search("platform", "tattoo piercing deferral")
    assert medical and all(c.domain == "medical" for c in medical)
    assert all(c.domain == "platform" for c in platform)


class _FakeEmbedder:
    def embed_query(self, query):
        return [0.0]


class _FakeCollection:
    def __init__(self, ids, distances):
        self.result = {"ids": [ids], "distances": [distances]}

    def query(self, **kwargs):
        return self.result


def _vector_store(distances):
    store = KnowledgeStore()
    ids = [c.id for c in store.chunks("medical")[:len(distances)]]
    store.vector_ready["medical"] = True
    store._embedder = _FakeEmbedder()
    store._collection = lambda domain: _FakeCollection(ids, distances)
    return store, ids


def test_vector_hits_beyond_the_cut_off_are_dropped():
    store, ids = _vector_store([0.2, 0.3, 0.6])
    assert [c.id for c in store.search("medical", "anything")] == ids[:2]


def test_off_topic_vector_results_do_not_fall_back_to_keywords():
    store, _ = _vector_store([0.47, 0.48])
    # "stored" and "expires" would match storage guidance by keyword, but the vectors say it is not close enough
    assert store.search("medical", "How long is blood stored before it expires?") == []


def test_keyword_retrieval_finds_the_relevant_guidance():
    store = KnowledgeStore()
    top = store.search("medical", "How long is blood stored before it expires? shelf life CPDA")[0]
    assert "storage" in top.title.lower() or "shelf" in top.text.lower()
    transfer = store.search("platform", "offer blood to another hospital transfer")[0]
    assert "transfer" in transfer.text.lower()
