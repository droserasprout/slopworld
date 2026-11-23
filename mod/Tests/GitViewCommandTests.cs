using System;
using System.IO;
using NUnit.Framework;
using static SlopWorld.Tests.ViewCommandFixture;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class GitViewCommandTests
    {
        [TestCase("AA")]
        [TestCase("DD")]
        [TestCase("AU")]
        [TestCase("UD")]
        [TestCase("UA")]
        [TestCase("DU")]
        [TestCase("UU")]
        public void AllPorcelainConflictPairsRemainUnmerged(string pair) =>
            Assert.That(new GitStatus(pair).Unmerged, Is.True);

        [TestCase("??", false, true, true)]
        [TestCase("A ", true, false, true)]
        [TestCase(" M", false, true, true)]
        [TestCase(" D", false, true, false)]
        [TestCase("D ", true, false, false)]
        [TestCase("DM", true, true, true)]
        public void GitActionsUseBothStatusColumns(
            string pair,
            bool staged,
            bool needsStage,
            bool present
        )
        {
            var status = new GitStatus(pair);
            Assert.That(status.IsStaged, Is.EqualTo(staged));
            Assert.That(status.NeedsStage, Is.EqualTo(needsStage));
            Assert.That(status.Present, Is.EqualTo(present));
        }

        [TestCase("sha1")]
        [TestCase("sha256")]
        public void UnbornDiffUsesTheEmptyTree(string format)
        {
            WithRepository(
                format,
                root =>
                {
                    const string name = "file ' $(no-command).txt";
                    string path = Path.Combine(root, name);
                    File.WriteAllText(path, "first\n");
                    Git(root, "add -- " + PagerCommands.Quote(name));
                    File.WriteAllText(path, "edited after staging\n");
                    Assert.That(
                        Run(GitCommands.SectionDiff(root, new[] { name }, true)),
                        Does.Contain("first").And.Not.Contain("edited after staging")
                    );
                    Assert.That(
                        Run(GitCommands.SectionDiff(root, new[] { name }, false)),
                        Does.Contain("edited after staging")
                    );
                    string command = GitCommands.Diff(root, name, false);
                    string diff = Run(command);
                    Assert.That(diff, Does.Contain("edited after staging"));
                    Assert.That(
                        Run(GitCommands.Diff(root, null, false)),
                        Does.Contain("edited after staging")
                    );
                    var configured = PagerCommands.DiffCommand(
                        command,
                        "less",
                        false,
                        "test-theme"
                    );
                    Assert.That(configured, Does.Contain("delta.line-numbers=false"));
                    Assert.That(
                        PagerCommands.DiffCommand(configured, "less", false, "test-theme"),
                        Is.EqualTo(configured)
                    );
                    Git(root, "--literal-pathspecs reset -- " + PagerCommands.Quote(name));
                    Assert.That(File.ReadAllText(path), Is.EqualTo("edited after staging\n"));
                    Assert.That(Git(root, "ls-files"), Is.Empty);
                    Assert.That(
                        Run(GitCommands.Diff(root, name, true), 1),
                        Does.Contain("edited after staging")
                    );

                    Git(root, "add --all");
                    Git(root, "reset -- .");
                    Assert.That(Git(root, "ls-files"), Is.Empty);
                    Assert.That(File.Exists(path), Is.True);
                }
            );
        }

        [Test]
        public void CommittedDiffUsesHeadBaseline()
        {
            WithRepository(
                "sha1",
                root =>
                {
                    File.WriteAllText(Path.Combine(root, "tracked"), "original\n");
                    Git(root, "add --all");
                    Git(
                        root,
                        "-c user.name=Test -c user.email=test@example.invalid commit -qm initial"
                    );
                    File.WriteAllText(Path.Combine(root, "tracked"), "changed\n");
                    Git(root, "add --all");
                    string diff = Run(GitCommands.Diff(root, "tracked", false));
                    Assert.That(diff, Does.Contain("original").And.Contain("changed"));
                    Git(root, "reset -- .");
                    Assert.That(Git(root, "diff --cached --name-only"), Is.Empty);
                    Assert.That(
                        File.ReadAllText(Path.Combine(root, "tracked")),
                        Is.EqualTo("changed\n")
                    );
                    Assert.That(Git(root, "ls-files").Trim(), Is.EqualTo("tracked"));
                }
            );
        }

        [TestCase("sha1")]
        [TestCase("sha256")]
        public void LiteralPathsKeepOtherFilesOutOfDiffs(string format)
        {
            WithRepository(
                format,
                root =>
                {
                    const string name = ":(glob)*.txt";
                    File.WriteAllText(Path.Combine(root, name), "selected\n");
                    File.WriteAllText(Path.Combine(root, "other.txt"), "unrelated\n");
                    Git(root, "--literal-pathspecs add -- " + PagerCommands.Quote(name));
                    Assert.That(Git(root, "ls-files").Trim(), Is.EqualTo(name));
                    Assert.That(
                        Run(GitCommands.Diff(root, name, false)),
                        Does.Not.Contain("unrelated")
                    );
                    Git(root, "add --all");
                    Git(root, "--literal-pathspecs reset -- " + PagerCommands.Quote(name));
                    Assert.That(Git(root, "ls-files").Trim(), Is.EqualTo("other.txt"));
                    Git(root, "add --all");
                    Git(
                        root,
                        "-c user.name=Test -c user.email=test@example.invalid -c commit.gpgsign=false commit -qm initial"
                    );
                    File.AppendAllText(Path.Combine(root, name), "selected edit\n");
                    File.AppendAllText(Path.Combine(root, "other.txt"), "unrelated edit\n");
                    Git(root, "--literal-pathspecs add -- " + PagerCommands.Quote(name));
                    Assert.That(Git(root, "diff --cached --name-only").Trim(), Is.EqualTo(name));
                    Assert.That(
                        Run(GitCommands.Diff(root, name, false)),
                        Does.Not.Contain("unrelated edit")
                    );
                    Git(root, "add --all");
                    Git(root, "--literal-pathspecs reset -- " + PagerCommands.Quote(name));
                    Assert.That(
                        Git(root, "diff --cached --name-only").Trim(),
                        Is.EqualTo("other.txt")
                    );
                }
            );
        }

        [TestCase("destination/new.txt")]
        [TestCase("destination")]
        [TestCase(null)]
        public void RenameDiffIncludesSourceOutsideSelectedPath(string selection)
        {
            WithRepository(
                "sha1",
                root =>
                {
                    File.WriteAllText(Path.Combine(root, "old.txt"), "original\n");
                    File.WriteAllText(Path.Combine(root, "other.txt"), "before\n");
                    Git(root, "add --all");
                    Git(
                        root,
                        "-c user.name=Test -c user.email=test@example.invalid -c commit.gpgsign=false commit -qm initial"
                    );
                    Directory.CreateDirectory(Path.Combine(root, "destination"));
                    Git(root, "mv old.txt destination/new.txt");
                    File.WriteAllText(Path.Combine(root, "other.txt"), "unrelated edit\n");
                    var repo = new GitStore.Repo { Root = root };
                    repo.Changes[("destination/new.txt", false)] = new Wire.GitFile
                    {
                        Path = "destination/new.txt",
                        Status = "R ",
                        Source = "old.txt",
                    };
                    string diff = Run(GitCommands.Diff(root, GitStore.DiffPaths(repo, selection)));
                    Assert.That(diff, Does.Contain("rename from old.txt"));
                    Assert.That(diff, Does.Contain("rename to destination/new.txt"));
                    Assert.That(diff, Does.Not.Contain("new file mode"));
                    Assert.That(
                        diff,
                        selection == null
                            ? Does.Contain("unrelated edit")
                            : Does.Not.Contain("unrelated edit")
                    );
                }
            );
        }

        static void WithRepository(string format, Action<string> body)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "slopworld view ' " + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            try
            {
                Git(root, "init -q --object-format=" + format);
                body(root);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        static string Git(string root, string command) =>
            Run("git -C " + PagerCommands.Quote(root) + " " + command);
    }
}
