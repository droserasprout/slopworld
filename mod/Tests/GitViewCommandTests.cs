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
        public void UnbornDiffAndUnstagePreserveWorkingFiles(string format)
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
                    Run(GitCommands.Unstage(root, name));
                    Assert.That(File.ReadAllText(path), Is.EqualTo("edited after staging\n"));
                    Assert.That(Git(root, "ls-files"), Is.Empty);
                    Assert.That(
                        Run(GitCommands.Diff(root, name, true), 1),
                        Does.Contain("edited after staging")
                    );

                    Git(root, "add --all");
                    Run(GitCommands.Unstage(root, "."));
                    Assert.That(Git(root, "ls-files"), Is.Empty);
                    Assert.That(File.Exists(path), Is.True);
                }
            );
        }

        [Test]
        public void CommittedDiffAndUnstageKeepHeadBaseline()
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
                    Run(GitCommands.Unstage(root, "."));
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
        public void LiteralPathsKeepOtherFilesOutOfActions(string format)
        {
            WithRepository(
                format,
                root =>
                {
                    const string name = ":(glob)*.txt";
                    File.WriteAllText(Path.Combine(root, name), "selected\n");
                    File.WriteAllText(Path.Combine(root, "other.txt"), "unrelated\n");
                    Run(GitCommands.Stage(root, name));
                    Assert.That(Git(root, "ls-files").Trim(), Is.EqualTo(name));
                    Assert.That(
                        Run(GitCommands.Diff(root, name, false)),
                        Does.Not.Contain("unrelated")
                    );
                    Git(root, "add --all");
                    Run(GitCommands.Unstage(root, name));
                    Assert.That(Git(root, "ls-files").Trim(), Is.EqualTo("other.txt"));
                    Git(root, "add --all");
                    Git(
                        root,
                        "-c user.name=Test -c user.email=test@example.invalid -c commit.gpgsign=false commit -qm initial"
                    );
                    File.AppendAllText(Path.Combine(root, name), "selected edit\n");
                    File.AppendAllText(Path.Combine(root, "other.txt"), "unrelated edit\n");
                    Run(GitCommands.Stage(root, name));
                    Assert.That(Git(root, "diff --cached --name-only").Trim(), Is.EqualTo(name));
                    Assert.That(
                        Run(GitCommands.Diff(root, name, false)),
                        Does.Not.Contain("unrelated edit")
                    );
                    Git(root, "add --all");
                    Run(GitCommands.Unstage(root, name));
                    Assert.That(
                        Git(root, "diff --cached --name-only").Trim(),
                        Is.EqualTo("other.txt")
                    );
                }
            );
        }

        [Test]
        public void UnstageRenameRestoresBothIndexPathsWithoutMovingWorkingFile()
        {
            WithRepository(
                "sha1",
                root =>
                {
                    File.WriteAllText(Path.Combine(root, "old.txt"), "original\n");
                    Git(root, "add --all");
                    Git(
                        root,
                        "-c user.name=Test -c user.email=test@example.invalid -c commit.gpgsign=false commit -qm initial"
                    );
                    Directory.CreateDirectory(Path.Combine(root, "target"));
                    Git(root, "mv old.txt target/new.txt");
                    Run(GitCommands.Unstage(root, "target/new.txt", "old.txt"));
                    Assert.That(Git(root, "diff --cached --name-only"), Is.Empty);
                    Assert.That(File.Exists(Path.Combine(root, "old.txt")), Is.False);
                    Assert.That(
                        File.ReadAllText(Path.Combine(root, "target/new.txt")),
                        Is.EqualTo("original\n")
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
