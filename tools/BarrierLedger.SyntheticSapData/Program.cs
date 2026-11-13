using System.Globalization;

namespace BarrierLedger.SyntheticSapData;

internal static class Program
{
    private const string Usage = """
        Synthetic SAP master data for the Barrier Ledger SAP import module.

        Usage:
          synthetic-sap-data generate --out <dir> [--seed <n>]
              Write the dataset files and manifest to <dir>.

          synthetic-sap-data verify --dir <dir>
              Re-derive every seeded condition from the files in <dir> and compare
              with the manifest and the spec counts.

          synthetic-sap-data check --fixtures <dir>
              Generate with the default seed, verify the result, confirm a second
              run is byte-identical, and confirm the committed fixtures in <dir>
              are up to date. This is what CI runs.

        Exit code 0 means every check passed.
        """;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                Console.WriteLine(Usage);
                return 2;
            }

            Dictionary<string, string> options = ParseOptions(args.Skip(1).ToArray());
            switch (args[0])
            {
                case "generate":
                    return Generate(Required(options, "out"), Seed(options));
                case "verify":
                    return Verify(Required(options, "dir"));
                case "check":
                    return CheckFixtures(Required(options, "fixtures"));
                default:
                    Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int Generate(string directory, ulong seed)
    {
        Dataset data = DatasetBuilder.Build(seed);
        Dictionary<string, byte[]> files = DatasetWriter.Render(data);
        DatasetWriter.WriteTo(directory, files);
        Console.WriteLine($"Seed {seed}: {data.EmittedFls.Count()} FL rows, {data.Equipment.Count} equipment rows.");
        foreach (SeededDefect defect in data.Defects)
        {
            Console.WriteLine($"  {defect.Id}  {defect.ExpectedCount,5} {defect.CountUnit,-20} {defect.Title}");
        }

        Console.WriteLine($"Wrote {files.Count} files to {Path.GetFullPath(directory)}");
        return 0;
    }

    private static int Verify(string directory)
    {
        var verifier = new Verifier(Verifier.Load(directory));
        verifier.Run();
        return Report(verifier.Results.ToList());
    }

    private static int CheckFixtures(string fixtureDirectory)
    {
        Dictionary<string, byte[]> first = DatasetWriter.Render(DatasetBuilder.Build(DatasetBuilder.DefaultSeed));
        Dictionary<string, byte[]> second = DatasetWriter.Render(DatasetBuilder.Build(DatasetBuilder.DefaultSeed));

        var verifier = new Verifier(first);
        verifier.Run();
        var results = verifier.Results.ToList();

        string[] unstable = FileNames.ByteStable.Where(n => !first[n].AsSpan().SequenceEqual(second[n])).ToArray();
        results.Add(("F01 two runs with the same seed give byte-identical .csv and manifest files",
            unstable.Length == 0, string.Join(", ", unstable)));

        Dictionary<string, byte[]> other = DatasetWriter.Render(DatasetBuilder.Build(DatasetBuilder.DefaultSeed + 1));
        results.Add(("F02 a different seed gives different data",
            !first[FileNames.EquipmentCsv].AsSpan().SequenceEqual(other[FileNames.EquipmentCsv]), ""));
        var otherVerifier = new Verifier(other);
        otherVerifier.Run();
        results.Add(("F03 a different seed still satisfies every check",
            otherVerifier.AllPassed,
            string.Join("; ", otherVerifier.Results.Where(r => !r.Passed).Select(r => r.Name))));

        Dictionary<string, byte[]> committed = Verifier.Load(fixtureDirectory);
        string[] stale = FileNames.ByteStable
            .Where(n => !committed.TryGetValue(n, out byte[]? bytes) || !bytes.AsSpan().SequenceEqual(first[n]))
            .ToArray();
        results.Add(("F04 committed .csv and manifest fixtures are byte-identical to a fresh run",
            stale.Length == 0, string.Join(", ", stale)));

        var committedVerifier = new Verifier(committed);
        committedVerifier.Run();
        results.Add(("F05 committed fixtures, including the .xlsx files, satisfy every check",
            committedVerifier.AllPassed,
            string.Join("; ", committedVerifier.Results.Where(r => !r.Passed).Select(r => r.Name))));

        return Report(results);
    }

    private static int Report(List<(string Name, bool Passed, string Detail)> results)
    {
        foreach ((string name, bool passed, string detail) in results)
        {
            string suffix = detail.Length > 0 ? $"  ({detail})" : "";
            Console.WriteLine($"{(passed ? "PASS" : "FAIL")}  {name}{(passed && detail.Length > 60 ? "" : suffix)}");
        }

        int failed = results.Count(r => !r.Passed);
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {results.Count} checks passed."
            : $"{failed} of {results.Count} checks FAILED.");
        return failed == 0 ? 0 : 1;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value', got '{args[i]}'.");
            }

            options[args[i][2..]] = args[i + 1];
        }

        return options;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value)
            ? value
            : throw new ArgumentException($"Missing required option --{name}.");

    private static ulong Seed(Dictionary<string, string> options) =>
        options.TryGetValue("seed", out string? text)
            ? ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed)
                ? seed
                : throw new ArgumentException($"--seed must be a non-negative integer, got '{text}'.")
            : DatasetBuilder.DefaultSeed;
}
