using System.Text.Json.Nodes;
using KnS.Protocol;
using KnS.Protocol.Json;

namespace KnS.Tests.Protocol;

public class RequestParametersTests
{
    private static RequestParameters Parse(string json) => new(JsonNode.Parse(json)!.AsObject());

    [Fact]
    public void Unread_names_are_the_parameters_nothing_looked_at()
    {
        RequestParameters parameters = Parse("""{"a": 1, "b": [], "c": "x", "d": 0}""");

        parameters.GetInt64("a");
        parameters.GetList("b");
        parameters.Contains("missing");

        Assert.Equal(["c", "d"], parameters.UnreadNames);
    }

    [Theory]
    [InlineData("""{"n": 0}""")]
    [InlineData("""{"n": ""}""")]
    [InlineData("""{"n": null}""")]
    [InlineData("""{"n": false}""")]
    [InlineData("""{"n": []}""")]
    [InlineData("""{}""")]
    public void A_value_that_is_not_set_falls_back(string json)
    {
        Assert.Equal(7, Parse(json).GetInt64("n", 7));
    }

    [Theory]
    [InlineData("""{"n": 12}""", 12)]
    [InlineData("""{"n": "12"}""", 12)]
    [InlineData("""{"n": " -3 "}""", -3)]
    [InlineData("""{"n": 4.9}""", 4)]
    [InlineData("""{"n": true}""", 1)]
    public void Integers_convert_like_python_int(string json, long expected)
    {
        Assert.Equal(expected, Parse(json).GetInt64("n"));
    }

    [Fact]
    public void A_set_value_that_is_not_an_integer_throws_or_falls_back()
    {
        RequestParameters parameters = Parse("""{"n": "abc"}""");

        Assert.Throws<FormatException>(() => parameters.GetInt64("n"));
        Assert.Equal(5, parameters.GetInt64OrFallback("n", 5));
    }

    [Fact]
    public void Text_converts_like_python_str()
    {
        RequestParameters parameters = Parse("""{"a": 123, "b": true, "c": "x", "d": ""}""");

        Assert.Equal("123", parameters.GetText("a", "-"));
        Assert.Equal("True", parameters.GetText("b", "-"));
        Assert.Equal("x", parameters.GetText("c", "-"));
        Assert.Equal("-", parameters.GetText("d", "-"));
    }

    [Theory]
    [InlineData("5", true, 5)]
    [InlineData("5.0", false, 0)]
    [InlineData("\"5\"", false, 0)]
    [InlineData("true", true, 1)]
    public void Only_json_integers_count_as_integers(string json, bool isInteger, long expected)
    {
        Assert.Equal(isInteger, LenientJson.IsInteger(JsonNode.Parse(json), out long number));
        Assert.Equal(expected, number);
    }

    [Fact]
    public void Form_bodies_keep_the_first_value_and_drop_blank_ones()
    {
        JsonObject fields = FormBody.Parse("a=1&a=2&b=&c=x+y%21&d"u8);

        Assert.Equal("1", fields["a"]!.GetValue<string>());
        Assert.False(fields.ContainsKey("b"));
        Assert.Equal("x y!", fields["c"]!.GetValue<string>());
        Assert.False(fields.ContainsKey("d"));
    }
}
