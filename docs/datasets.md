# Evaluation datasets

Every number in this repository comes from public datasets with queries, a document corpus
and relevance labels, whose licences allow publishing results. The data is **downloaded,
never committed**: [`datasets/datasets.json`](../datasets/datasets.json) pins every file by
SHA-256 (and the Hugging Face revision where there is one), and the download refuses a file
whose checksum differs.

| Id | Language | Documents | Test queries | Licence | Used for |
|---|---|---:|---:|---|---|
| `scifact` | English | 5,183 abstracts | 300 | CC BY 4.0 (claims, labels), ODC-By 1.0 (abstracts) | Retrieval experiments; comparison with published BEIR numbers |
| `turkish-rag-eval` | Turkish | 54 articles (1.09 M chars) | 58, hand-written | CC BY-SA 4.0 | Retrieval on long documents, chunking, multilingual embeddings; answer evaluation (answer spans) |
| `turkish-rag-eval-full` | Turkish | 94 articles (1.89 M chars) | 296 (58 human, 238 LLM-drafted) | CC BY-SA 4.0 | Secondary check across six domains; reported separately, as provisional |

## Why these

**SciFact** ([Wadden et al., 2020](https://aclanthology.org/2020.emnlp-main.609/), in its
[BEIR](https://github.com/beir-cellar/beir) form) is small enough to index on a laptop in
minutes, has expert relevance labels, and is one of the most reported BEIR tasks. Published
BM25 and dense-model scores on it are a sanity check for our own pipeline: a baseline far
from them points at a bug, not at a finding. Its queries are scientific claims rather than
questions, and its documents are short abstracts (about 1,500 characters), so it mostly
measures the embedding model and retrieval strategy, not chunking.

**Turkish RAG Eval** ([dataset card](https://huggingface.co/datasets/mteb/turkish-rag-eval),
[harness](https://github.com/RizgarOzan/turkish-rag-eval)) covers what SciFact does not:

- **Long documents** (about 20,000 characters per article), so chunking strategies actually
  differ. Half the articles are distractors from the same domain.
- **Turkish**, an agglutinative language where multilingual embedding models and
  stemming-free sparse retrieval are expected to behave differently from English.
- **Questions, not claims, with a verbatim answer span** each. The span lets us check
  retrieval at chunk level (did a retrieved chunk contain the answer?), and gives a
  reference for answer correctness (#16).
- Questions are **paraphrased** away from the article wording, so lexical retrieval gets no
  unearned advantage.

Its limits are stated by its author and apply here: 58 queries means differences below about
0.05 nDCG@10 are noise; one annotator, one pass; the 238 extra questions in the full set are
LLM-drafted and double-labelled, not human-verified, so they are reported apart from the 58
and marked provisional.

## Licence check

| Dataset | What the licence says | Source checked |
|---|---|---|
| SciFact | Claims and evidence annotations: **CC BY 4.0**. Abstracts: part of S2ORC, **ODC-By 1.0**. Both allow use, redistribution and publishing results with attribution. | [allenai/scifact LICENSE.md](https://github.com/allenai/scifact/blob/master/LICENSE.md) |
| Turkish RAG Eval | **CC BY-SA 4.0** (articles are Turkish Wikipedia text, itself CC BY-SA). Results can be published with attribution; redistributed data must keep the licence. | [Hugging Face dataset card](https://huggingface.co/datasets/mteb/turkish-rag-eval) |

The BEIR paper lists SciFact as CC BY-NC 2.0, and a derived Hugging Face card lists CC BY-SA
4.0; both disagree with the dataset's own licence file, which is what is followed here.

Considered and not used:

| Dataset | Why not |
|---|---|
| NFCorpus, FiQA-2018 (BEIR) | No licence reported by the BEIR authors. |
| ArguAna (BEIR) | CC BY 4.0, but the task is counter-argument retrieval, far from question answering. |
| Natural Questions, HotpotQA, MS MARCO | Millions of passages: too large to index repeatedly on a laptop; MS MARCO is non-commercial. |
| TR-MTEB translations (SciFact-TR, FiQA-TR, …) | Machine translations of the above; no licence stated for the translated sets. |
| TurHistQuad | MIT, but the corpus is short reading-comprehension contexts, so it says little about chunking. |

## How relevance is scored

Both datasets label **documents**, while the pipeline retrieves **chunks**. A document's
score for a query is the best score of any of its chunks, and documents are ranked by that
score: recall@k, MRR and nDCG@10 (#9) are computed on that ranking, as in BEIR.

For Turkish RAG Eval there is also a stricter, chunk-level measure, as in the dataset's own
harness: a retrieved chunk counts only if it comes from the right article and contains the
answer span. This is the measure that shows whether chunking keeps answers intact.

## Attribution

- SciFact: David Wadden, Shanchuan Lin, Kyle Lo, Lucy Lu Wang, Madeleine van Zuylen, Arman
  Cohan, Hannaneh Hajishirzi. *Fact or Fiction: Verifying Scientific Claims.* EMNLP 2020.
  Abstracts from S2ORC (Lo et al., ACL 2020), ODC-By 1.0.
- BEIR packaging: Nandan Thakur, Nils Reimers, Andreas Rücklé, Abhishek Srivastava, Iryna
  Gurevych. *BEIR: A Heterogeneous Benchmark for Zero-shot Evaluation of Information
  Retrieval Models.* NeurIPS 2021 Datasets and Benchmarks.
- Turkish RAG Eval: Rizgar Ozan, [turkish-rag-eval](https://github.com/RizgarOzan/turkish-rag-eval),
  CC BY-SA 4.0; article text from Turkish Wikipedia.
