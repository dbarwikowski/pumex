namespace Pumex.Daemon.Tests;

public class YamlFormatParserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pumex-yaml-" + Guid.NewGuid().ToString("N"));

    public YamlFormatParserTests() => Directory.CreateDirectory(_dir);

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
        var props = YamlFormatParser.ExtractTopLevelScalars(
            """
            title: Hello
            count: 42
            done: true
            off: false
            """);

        Assert.Equal("Hello", props["title"]);
        Assert.Equal("42", props["count"]);
        Assert.Equal("true", props["done"]);
        Assert.Equal("false", props["off"]);
    }

    [Fact]
    public void Scalar_values_keep_raw_text_with_no_type_inference()
    {
        // `yes` stays the string "yes" (not coerced to a bool); `3.14` stays raw
        // text so it is culture-invariant under InvariantGlobalization=false.
        var props = YamlFormatParser.ExtractTopLevelScalars(
            """
            enabled: yes
            pi: 3.14
            """);

        Assert.Equal("yes", props["enabled"]);
        Assert.Equal("3.14", props["pi"]);
    }

    [Fact]
    public void Quotes_are_stripped_from_scalar_values()
    {
        var props = YamlFormatParser.ExtractTopLevelScalars(
            """
            single: 'a value'
            double: "another"
            """);

        Assert.Equal("a value", props["single"]);
        Assert.Equal("another", props["double"]);
    }

    [Fact]
    public void Nested_mapping_sequence_and_null_are_not_properties()
    {
        var props = YamlFormatParser.ExtractTopLevelScalars(
            """
            scalar: x
            nada:
            tilde: ~
            obj:
              a: 1
            arr:
              - 1
              - 2
            """);

        Assert.True(props.ContainsKey("scalar"));
        Assert.False(props.ContainsKey("nada"));
        Assert.False(props.ContainsKey("tilde"));
        Assert.False(props.ContainsKey("obj"));
        Assert.False(props.ContainsKey("arr"));
        Assert.Single(props);
    }

    [Fact]
    public void Quoted_null_literal_is_a_real_string_property()
    {
        // `"null"` is a quoted scalar, not the YAML null — it stays a property.
        var props = YamlFormatParser.ExtractTopLevelScalars("""key: "null" """);
        Assert.Equal("null", props["key"]);
    }

    [Fact]
    public void Sequence_root_yields_no_properties()
    {
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars(
            """
            - one
            - two
            """));
    }

    [Fact]
    public void Scalar_root_yields_no_properties()
    {
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars("just a string"));
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars("42"));
    }

    [Fact]
    public void Duplicate_top_level_keys_are_invalid_yaml_so_no_properties()
    {
        // The YAML spec forbids duplicate mapping keys; YamlDotNet rejects them.
        // The document is treated as malformed → no properties (raw fallback).
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars(
            """
            k: first
            k: second
            """));
    }

    [Fact]
    public void Multi_document_file_uses_first_document_for_properties()
    {
        var props = YamlFormatParser.ExtractTopLevelScalars(
            """
            name: first-doc
            ---
            name: second-doc
            other: value
            """);

        Assert.Equal("first-doc", props["name"]);
        Assert.False(props.ContainsKey("other"));   // later docs do not contribute props
    }

    [Fact]
    public void Alias_to_a_scalar_resolves_to_the_anchored_value()
    {
        var props = YamlFormatParser.ExtractTopLevelScalars(
            """
            base: &shared hello
            ref: *shared
            """);

        Assert.Equal("hello", props["base"]);
        Assert.Equal("hello", props["ref"]);
    }

    [Fact]
    public void Polish_unicode_string_values_are_preserved()
    {
        var props = YamlFormatParser.ExtractTopLevelScalars("city: Łódź");
        Assert.Equal("Łódź", props["city"]);
    }

    [Fact]
    public void Malformed_yaml_yields_no_properties_and_does_not_throw()
    {
        // Unbalanced flow mapping — invalid YAML.
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars("key: {unterminated"));
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars(""));
        Assert.Empty(YamlFormatParser.ExtractTopLevelScalars("   "));
    }

    // --- Parse: NoteDocument shape -------------------------------------------

    [Fact]
    public void Parse_indexes_whole_file_as_body_with_no_tags_or_links()
    {
        var path = Write("data.yaml",
            """
            status: active
            nested:
              deep: value
            """);

        var doc = new YamlFormatParser().Parse(path);

        Assert.Equal("active", doc.Frontmatter["status"]);   // top-level scalar
        Assert.Contains("deep", doc.Content);                // nested still in FTS body
        Assert.Contains("value", doc.Content);
        Assert.Empty(doc.Tags);
        Assert.Empty(doc.OutgoingLinks);
    }

    [Fact]
    public void Parse_normalizes_crlf_to_lf_in_the_body()
    {
        var path = Write("crlf.yaml", "a: 1\r\nb: 2\r\n");
        var doc = new YamlFormatParser().Parse(path);
        Assert.DoesNotContain('\r', doc.Content);
    }

    [Fact]
    public void Malformed_file_is_indexed_as_raw_body_with_no_properties()
    {
        var path = Write("bad.yaml", "key: {unterminated");
        var doc = new YamlFormatParser().Parse(path);

        Assert.Empty(doc.Frontmatter);
        Assert.Contains("unterminated", doc.Content);   // raw text still searchable
    }

    [Fact]
    public void Registry_routes_dotyaml_and_dotyml_through_the_yaml_parser()
    {
        var registry = FormatParserRegistry.Default();

        var yaml = registry.Parse(Write("note.yaml", "title: T"));
        var yml = registry.Parse(Write("note.yml", "title: U"));

        Assert.Equal("T", yaml.Frontmatter["title"]);
        Assert.Equal("U", yml.Frontmatter["title"]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
