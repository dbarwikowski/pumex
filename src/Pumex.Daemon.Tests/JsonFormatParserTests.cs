namespace Pumex.Daemon.Tests;

public class JsonFormatParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pumex-json-" + Guid.NewGuid().ToString("N"));

    public JsonFormatParserTests() => Directory.CreateDirectory(_dir);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // --- ExtractTopLevelScalars: which keys become properties -----------------

    [Fact]
    public void Top_level_string_number_bool_become_properties()
    {
        var props = JsonFormatParser.ExtractTopLevelScalars(
            """{ "title": "Hello", "count": 42, "done": true, "off": false }""");

        Assert.Equal("Hello", props["title"]);
        Assert.Equal("42", props["count"]);
        Assert.Equal("true", props["done"]);
        Assert.Equal("false", props["off"]);
    }

    [Fact]
    public void Numbers_keep_raw_token_text_so_they_stay_culture_invariant()
    {
        // Stored as "3.14" regardless of the machine's decimal separator —
        // a C# double would round-trip to "3,14" under a pl-PL culture.
        var props = JsonFormatParser.ExtractTopLevelScalars("""{ "pi": 3.14 }""");
        Assert.Equal("3.14", props["pi"]);
    }

    [Fact]
    public void Null_and_nested_object_and_array_are_not_properties()
    {
        var props = JsonFormatParser.ExtractTopLevelScalars(
            """{ "scalar": "x", "nada": null, "obj": { "a": 1 }, "arr": [1, 2] }""");

        Assert.True(props.ContainsKey("scalar"));
        Assert.False(props.ContainsKey("nada"));
        Assert.False(props.ContainsKey("obj"));
        Assert.False(props.ContainsKey("arr"));
        Assert.Single(props);
    }

    [Fact]
    public void Array_root_yields_no_properties()
    {
        Assert.Empty(JsonFormatParser.ExtractTopLevelScalars("""[ { "a": 1 }, { "b": 2 } ]"""));
    }

    [Fact]
    public void Scalar_root_yields_no_properties()
    {
        Assert.Empty(JsonFormatParser.ExtractTopLevelScalars("42"));
        Assert.Empty(JsonFormatParser.ExtractTopLevelScalars("\"just a string\""));
    }

    [Fact]
    public void Duplicate_top_level_keys_last_one_wins()
    {
        var props = JsonFormatParser.ExtractTopLevelScalars("""{ "k": "first", "k": "second" }""");
        Assert.Equal("second", props["k"]);
    }

    [Fact]
    public void Jsonc_comments_and_trailing_commas_are_tolerated()
    {
        var props = JsonFormatParser.ExtractTopLevelScalars(
            """
            {
                // a line comment
                "name": "tsconfig",
                "strict": true, /* block comment */
            }
            """);

        Assert.Equal("tsconfig", props["name"]);
        Assert.Equal("true", props["strict"]);
    }

    [Fact]
    public void Polish_unicode_string_values_are_preserved()
    {
        var props = JsonFormatParser.ExtractTopLevelScalars("""{ "city": "Łódź" }""");
        Assert.Equal("Łódź", props["city"]);
    }

    [Fact]
    public void Malformed_json_yields_no_properties_and_does_not_throw()
    {
        Assert.Empty(JsonFormatParser.ExtractTopLevelScalars("{ this is not json"));
        Assert.Empty(JsonFormatParser.ExtractTopLevelScalars(""));
    }

    // --- Parse: NoteDocument shape -------------------------------------------

    [Fact]
    public void Parse_indexes_whole_file_as_body_with_no_tags_or_links()
    {
        var path = Write("data.json", """{ "status": "active", "nested": { "deep": "value" } }""");

        var doc = new JsonFormatParser().Parse(path);

        Assert.Equal("active", doc.Frontmatter["status"]);   // top-level scalar
        Assert.Contains("deep", doc.Content);                // nested still in FTS body
        Assert.Contains("value", doc.Content);
        Assert.Empty(doc.Tags);
        Assert.Empty(doc.OutgoingLinks);
    }

    [Fact]
    public void Parse_normalizes_crlf_to_lf_in_the_body()
    {
        var path = Write("crlf.json", "{\r\n  \"a\": 1\r\n}\r\n");
        var doc = new JsonFormatParser().Parse(path);
        Assert.DoesNotContain('\r', doc.Content);
    }

    [Fact]
    public void Malformed_file_is_indexed_as_raw_body_with_no_properties()
    {
        var path = Write("bad.json", "{ not valid json");
        var doc = new JsonFormatParser().Parse(path);

        Assert.Empty(doc.Frontmatter);
        Assert.Contains("not valid json", doc.Content);   // raw text still searchable
    }

    [Fact]
    public void Registry_routes_dotjson_through_the_json_parser()
    {
        var registry = FormatParserRegistry.Default();
        var path = Write("note.json", """{ "title": "T" }""");

        var doc = registry.Parse(path);

        Assert.Equal("T", doc.Frontmatter["title"]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
