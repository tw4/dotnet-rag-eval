using RagEval.Evaluation.Datasets;

namespace RagEval.Tests.Support;

/// <summary>Three documents and two questions with answer spans, in BEIR layout.</summary>
internal static class TinyDataset
{
    public static DatasetInfo Write(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "tiny", "qrels"));
        File.WriteAllText(Path.Combine(dir, "tiny/corpus.jsonl"), """
            {"_id": "pets", "title": "Pets", "text": "Cats sleep most of the day. Dogs enjoy long walks in the park."}
            {"_id": "space", "title": "Space", "text": "The moon orbits the earth. Mars has two small moons called Phobos and Deimos."}
            {"_id": "food", "title": "Food", "text": "Bread is baked from flour, water and yeast."}
            """);
        File.WriteAllText(Path.Combine(dir, "tiny/queries.jsonl"), """
            {"_id": "q1", "text": "how many moons does mars have", "answer_span": "Mars has two small moons"}
            {"_id": "q2", "text": "what do dogs enjoy", "answer_span": "Dogs enjoy long walks"}
            """);
        File.WriteAllText(Path.Combine(dir, "tiny/qrels/test.jsonl"), """
            {"query-id": "q1", "corpus-id": "space", "score": 1}
            {"query-id": "q2", "corpus-id": "pets", "score": 1}
            """);
        return new DatasetInfo("tiny", "tiny", "en", "t", "x", "x", "x", "beir-jsonl", "test", [], new DatasetSize(3, 2, 2));
    }
}
