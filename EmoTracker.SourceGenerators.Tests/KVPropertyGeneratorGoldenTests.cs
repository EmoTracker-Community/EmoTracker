using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Xunit;

namespace EmoTracker.SourceGenerators.Tests
{
    /// <summary>
    /// Golden-file test for the KV property generator: drives the generator over a
    /// canned input via the Roslyn driver API and compares its emitted source against
    /// a checked-in expected file under <c>Snapshots/</c>.
    ///
    /// <para>
    /// The expected file is committed as a regular C# string resource (UTF-8). When
    /// the generator's output legitimately changes, run the test, copy the actual
    /// output from the assertion failure into the snapshot file, and re-run.
    /// </para>
    /// </summary>
    public class KVPropertyGeneratorGoldenTests
    {
        const string Input = """
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;

namespace Demo
{
    public partial class GoldenSmoke : TransactableModelTypeBase
    {
        [KVImmutable]
        public partial string Tag { get; }

        [KVMutable]
        public partial int Count { get; set; }

        [KVMutable]
        [OnChanged(nameof(Bumped))]
        public partial string Note { get; set; }

        [KVTransactable]
        public partial bool Toggle { get; set; }

        [KVTransactable]
        [OnChanged(nameof(SelectedNoticed))]
        public partial string Selected { get; set; }

        [KVOverridable]
        public partial double Width { get; set; }

        [KVOverridable]
        [OnChanged(nameof(BackgroundChanged))]
        public partial string Background { get; set; }

        protected void Bumped() { }
        protected void SelectedNoticed() { }
        protected void BackgroundChanged() { }

        public override ModelTypeBase Fork()
        {
            var copy = new GoldenSmoke();
            copy.InitializeAsForkOf(this);
            return copy;
        }
    }
}
""";

        [Fact]
        public void GeneratorOutputMatchesGoldenSnapshot()
        {
            var (generated, diagnostics) = RunGenerator(Input);

            // No errors are allowed. (Warnings — e.g. RS-class — are tolerated.)
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            Assert.True(errors.Length == 0,
                "Generator emitted errors:\n" + string.Join("\n", errors.Select(e => e.ToString())));

            // Expect exactly one generated file (one type with KV-attributed properties).
            Assert.Single(generated);
            var actual = generated[0].text;

            // Load the committed golden snapshot, normalize line endings, compare.
            var goldenPath = LocateSnapshot("GoldenSmoke.kvprops.g.cs");
            var expected = File.ReadAllText(goldenPath);
            string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd();

            if (Normalize(expected) != Normalize(actual))
            {
                // Make the diff helpful by including the actual output verbatim.
                throw new Xunit.Sdk.XunitException(
                    "Generator output does not match the golden snapshot.\n" +
                    "Snapshot path: " + goldenPath + "\n\n" +
                    "If the change is intentional, replace the snapshot file with the" +
                    " 'Actual' block below.\n\n" +
                    "==== Expected (snapshot) ====\n" + expected + "\n" +
                    "==== Actual (generator) =====\n" + actual + "\n");
            }
        }

        [Fact]
        public void GeneratorEmitsErrorWhenContainerNotPartial()
        {
            const string nonPartial = """
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
namespace D
{
    public class NotPartial : TransactableModelTypeBase
    {
        [KVMutable]
        public partial int Count { get; set; }
        public override ModelTypeBase Fork() => null;
    }
}
""";
            var (_, diagnostics) = RunGenerator(nonPartial);
            Assert.Contains(diagnostics, d => d.Id == "EMODM003");
        }

        [Fact]
        public void GeneratorEmitsErrorWhenKVImmutableHasSetter()
        {
            const string immutableWithSetter = """
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
namespace D
{
    public partial class C : TransactableModelTypeBase
    {
        [KVImmutable]
        public partial string Tag { get; set; }
        public override ModelTypeBase Fork() => null;
    }
}
""";
            var (_, diagnostics) = RunGenerator(immutableWithSetter);
            Assert.Contains(diagnostics, d => d.Id == "EMODM004");
        }

        [Fact]
        public void GeneratorEmitsErrorWhenKVTransactableOnNonTransactableBase()
        {
            const string transactableOnPlainBase = """
using EmoTracker.Core.DataModel;
namespace D
{
    public partial class C : ModelTypeBase
    {
        [KVTransactable]
        public partial bool Toggle { get; set; }
        public override ModelTypeBase Fork() => null;
    }
}
""";
            var (_, diagnostics) = RunGenerator(transactableOnPlainBase);
            Assert.Contains(diagnostics, d => d.Id == "EMODM006");
        }

        [Fact]
        public void GeneratorEmitsErrorWhenKVMutablePropertyHasNoGetter()
        {
            // C# 14 partial properties allow set-only declarations syntactically,
            // but the generator can't emit a useful body without a getter — both
            // halves are needed for the dual-storage / setter-with-INPC pattern.
            const string setOnly = """
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
namespace D
{
    public partial class C : TransactableModelTypeBase
    {
        [KVMutable]
        public partial int X { set; }
        public override ModelTypeBase Fork() => null;
    }
}
""";
            var (_, diagnostics) = RunGenerator(setOnly);
            Assert.Contains(diagnostics, d => d.Id == "EMODM009");
        }

        [Fact]
        public void GeneratorEmitsErrorWhenOnChangedTargetMissing()
        {
            // Roslyn will already diagnose nameof() against an unknown name as a
            // language error; the generator must also surface its own diagnostic
            // when handed a literal string that doesn't resolve.
            const string missingViaString = """
using EmoTracker.Core.DataModel;
using EmoTracker.Data.Core.DataModel;
namespace D
{
    public partial class C : TransactableModelTypeBase
    {
        [KVMutable]
        [OnChanged("DefinitelyNotAMethod")]
        public partial int X { get; set; }
        public override ModelTypeBase Fork() => null;
    }
}
""";
            var (_, diagnostics) = RunGenerator(missingViaString);
            Assert.Contains(diagnostics, d => d.Id == "EMODM008");
        }

        // -----------------------------------------------------------------------

        static (ImmutableArray<(string hint, string text)>, ImmutableArray<Diagnostic>) RunGenerator(string source)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source, System.Text.Encoding.UTF8));

            // References: every assembly currently loaded in the test process. This
            // is over-broad, but cheap and gives us reliable type lookups for the
            // attributes (in EmoTracker.Core) and the model-type bases
            // (in EmoTracker.Data) without hand-rolling reference paths.
            var refs = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                "GoldenTest",
                new[] { syntaxTree },
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var generator = new KVPropertyGenerator();
            CSharpGeneratorDriver
                .Create(generator)
                .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var driverDiagnostics);

            var generatorDiags = driverDiagnostics;
            var emitted = updated.SyntaxTrees
                .Where(t => t != syntaxTree)
                .Select(t => (hint: Path.GetFileName(t.FilePath), text: t.ToString()))
                .ToImmutableArray();

            return (emitted, generatorDiags);
        }

        static string LocateSnapshot(string fileName)
        {
            // Walk up from the running test assembly until we find the project's
            // Snapshots/ folder. This avoids a hard-coded MSBuild content path.
            string dir = Path.GetDirectoryName(typeof(KVPropertyGeneratorGoldenTests).Assembly.Location);
            for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                var candidate = Path.Combine(dir, "Snapshots", fileName);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Snapshot not found: " + fileName);
        }
    }
}
