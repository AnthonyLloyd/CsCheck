using System.Text;
using System.Text.Json;
using CsCheck;

namespace Tests;

public class GenLogsTest
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
        var processor = Check.GenLogger.LogProcessor.Tyche;
        using (MemoryStream memoryStream = new())
        {
            using (StreamWriter writer = new StreamWriter(memoryStream))
            {
                var logParameters = new Check.GenLogger.GenLogParameters(writer, processor, "Bool_Distribution_WithMetrics");
                const int frequency = 10;
                var expected = Enumerable.Repeat(frequency, 2).ToArray();
                try
                {
                    Gen.Bool.Select(i => i ? generatedIntUponTrue : 0).Array[2 * frequency]
                        .Select(sample => Tally(2, sample))
                        .Sample(actual => Check.ChiSquared(expected, actual, 10), iter: 100, time: -2,
                            genLogParameters: logParameters);
                }
                catch
                {
                }

                memoryStream.Position = 0;
                string json;
                using (StreamReader reader = new StreamReader(memoryStream, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                var tycheData = JsonSerializer.Deserialize<List<Check.GenLogger.TycheData<int[]>>>(json);
                Assert.Equal(tycheData?.TrueForAll(
                    td =>
                        td.type == "test_case" &&
                        td.status == (generatedIntUponTrue == 1 ? "passed" : "failed") &&
                        td.property == "Bool_Distribution_WithMetrics" &&
                        td.representation[0] + td.representation[1] == 20 &&
                        td.how_generated == "testing"
                ),true);
            }
        }
    }
}