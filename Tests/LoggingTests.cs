namespace Tests;
using System.Text;
using System.Text.Json;
using CsCheck;

public class LoggingTests
{
    static int[] Tally(int n, int[] ia)
    {
        var a = new int[n];
        for (int i = 0; i < ia.Length; i++) a[ia[i]]++;
        return a;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Bool_Distribution_WithTycheLogs(int generatedIntUponTrue)
    {
        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream);
        writer.AutoFlush = true;
        var logger = Logging.CreateTycheLogger(writer: writer);

        // Random test logic
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, 2).ToArray();

        //Try catch to suppress failing original test logic
        try
        {
            Gen.Bool.Select(i => i ? generatedIntUponTrue : 0).Array[2 * frequency]
                .Select(sample => Tally(2, sample))
                .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2, logger: logger);
        }
        catch
        {
        }

        //Actual logic we want to test.
        memoryStream.Position = 0;
        string json;
        using (var reader = new StreamReader(memoryStream, Encoding.UTF8))
        {
            json = reader.ReadToEnd();
        }

        var tycheData = JsonSerializer.Deserialize<TycheData>(json);

        Assert.True(tycheData != null && LogCheck(tycheData, generatedIntUponTrue));
        Assert.Equal(20, JsonSerializer.Deserialize<int[]>(tycheData.representation)?.Sum());

        static bool LogCheck(TycheData td, int generatedIntUponTrue)
        {
            return
                td.type == "test_case" &&
                td.status == (generatedIntUponTrue == 1 ? "passed" : "failed") &&
                td.property == "Bool_Distribution_WithTycheLogs" &&
                td.how_generated == "testing";
        }
    }

    //Test below can be used to see example of output
    [Theory(Skip = "Only run if you want to verify Tyche output")]
    [InlineData(1)]
    public void Bool_Distribution_WithTycheLogs_ToFile(int generatedIntUponTrue)
    {
        var logger = Logging.CreateTycheLogger();
        // Random test logic
        const int frequency = 10;
        var expected = Enumerable.Repeat(frequency, 2).ToArray();
        //Try catch to suppress failing original test logic
        try
        {
            Gen.Bool.Select(i => i ? generatedIntUponTrue : 0).Array[2 * frequency]
                .Select(sample => Tally(2, sample))
                .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 1, time: -2, logger: logger);
        }
        catch {}
    }
}