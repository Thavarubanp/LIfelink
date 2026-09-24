"""Build or refresh the ChromaDB knowledge collections: python -m knowledge.ingest [--rebuild]"""
import argparse
import json
import logging

from knowledge.store import knowledge_store

if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(levelname)s %(name)s: %(message)s")
    parser = argparse.ArgumentParser(description="Embed LifeLink knowledge documents into ChromaDB.")
    parser.add_argument("--rebuild", action="store_true", help="Drop and re-embed every chunk.")
    args = parser.parse_args()
    print(json.dumps(knowledge_store.ingest(rebuild=args.rebuild), indent=2))
