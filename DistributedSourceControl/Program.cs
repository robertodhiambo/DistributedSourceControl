using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

public class MiniGitApp
{
    private readonly string repoPath;
    private readonly string gitPath;
    private readonly string objectsPath;
    private readonly string indexPath;
    private readonly string headPath;
    private readonly string branchesPath;
    private readonly string ignoreFile;

    public MiniGitApp(string path)
    {
        repoPath = Path.GetFullPath(path);
        gitPath = Path.Combine(repoPath, ".minigit");
        objectsPath = Path.Combine(gitPath, "objects");
        indexPath = Path.Combine(gitPath, "index");
        headPath = Path.Combine(gitPath, "HEAD");
        branchesPath = Path.Combine(gitPath, "branches");
        ignoreFile = Path.Combine(gitPath, ".gitignore");
    }

    public void Initialize()
    {
        if (Directory.Exists(gitPath))
        {
            Console.WriteLine("Repository already exists");
        }
        else
        {
            Directory.CreateDirectory(objectsPath);
            Directory.CreateDirectory(branchesPath);
            File.WriteAllText(headPath, "main");
            File.WriteAllText(Path.Combine(branchesPath, "main"), "{}");
            File.WriteAllText(indexPath, "{}");
            File.WriteAllText(ignoreFile, "");
            Console.WriteLine($"Initialized empty MiniGit repository in {gitPath}");
        }
    }

    private string HashObject(string data)
    {
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(data));
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
            var objPath = Path.Combine(objectsPath, hashString);
            File.WriteAllText(objPath, data);
            return hashString;
        }
    }

    private List<string> GetIgnorePatterns()
    {
        if (!File.Exists(ignoreFile)) return new List<string>();
        return new List<string>(File.ReadAllLines(ignoreFile));
    }

    private bool IsIgnored(string filepath)
    {
        var ignorePatterns = GetIgnorePatterns();
        foreach (var pattern in ignorePatterns)
        {
            if (Regex.IsMatch(filepath, WildcardToRegex(pattern)))
            {
                return true;
            }
        }
        return false;
    }

    private string WildcardToRegex(string pattern)
    {
        return "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
    }

    public void Add(string filepath)
    {
        var fullPath = Path.GetFullPath(filepath);
        var relPath = Path.GetRelativePath(repoPath, fullPath);

        if (IsIgnored(relPath))
        {
            Console.WriteLine($"Ignored {relPath}");
            return;
        }

        var content = File.ReadAllText(fullPath);
        var objectHash = HashObject(content);
        var index = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(indexPath));
        index[relPath] = objectHash;
        File.WriteAllText(indexPath, JsonConvert.SerializeObject(index));
        Console.WriteLine($"Staged {relPath}");
    }

    public void Commit(string message)
    {
        var index = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(indexPath));
        var currentBranch = File.ReadAllText(headPath).Trim();
        var branchPath = Path.Combine(branchesPath, currentBranch);
        var branchData = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(branchPath));
        var commitData = new Dictionary<string, object>
        {
            ["message"] = message,
            ["changes"] = index,
            ["parent"] = branchData.ContainsKey("head") ? branchData["head"] : null
        };
        var commitHash = HashObject(JsonConvert.SerializeObject(commitData));
        branchData["head"] = commitHash;
        if (!branchData.ContainsKey("history")) branchData["history"] = new List<string>();
        ((List<string>)branchData["history"]).Add(commitHash);
        File.WriteAllText(branchPath, JsonConvert.SerializeObject(branchData));
        File.WriteAllText(indexPath, "{}");
        Console.WriteLine($"Committed with hash {commitHash}");
    }

    public void Log()
    {
        var currentBranch = File.ReadAllText(headPath).Trim();
        var branchPath = Path.Combine(branchesPath, currentBranch);
        var branchData = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(branchPath));

        if (branchData.ContainsKey("history"))
        {
            var history = (List<string>)branchData["history"];
            history.Reverse();

            foreach (var commitHash in history)
            {
                var commitData = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(Path.Combine(objectsPath, commitHash)));
                Console.WriteLine($"Commit: {commitHash}\nMessage: {commitData["message"]}\n");
            }
        }
    }

    public void Diff(string commitHash1, string commitHash2)
    {
        var commit1Path = Path.Combine(objectsPath, commitHash1);
        var commit2Path = Path.Combine(objectsPath, commitHash2);

        if (!File.Exists(commit1Path) || !File.Exists(commit2Path))
        {
            Console.WriteLine("One or both commits not found.");
            return;
        }

        var commit1 = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(commit1Path));
        var commit2 = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(commit2Path));

        var changes1 = JsonConvert.DeserializeObject<Dictionary<string, string>>(commit1["changes"].ToString());
        var changes2 = JsonConvert.DeserializeObject<Dictionary<string, string>>(commit2["changes"].ToString());

        Console.WriteLine("Diffs between commits");

        foreach (var file in changes1.Keys)
        {
            if (!changes2.ContainsKey(file))
            {
                Console.WriteLine($"Deleted: {file}");
            }
            else if (changes1[file] != changes2[file])
            {
                Console.WriteLine($"Modified: {file}");
            }
        }

        foreach (var file in changes2.Keys)
        {
            if (!changes1.ContainsKey(file))
            {
                Console.WriteLine($"Added: {file}");
            }
        }
    }

    public void Branch(string branchName)
    {
        var currentBranch = File.ReadAllText(headPath).Trim();
        var branchPath = Path.Combine(branchesPath, currentBranch);
        var branchData = File.ReadAllText(branchPath);

        File.WriteAllText(headPath, branchName);
        Console.WriteLine($"Switched to branch {branchName}");
    }

    public void Checkout(string name)
    {
        var branchPath = Path.Combine(branchesPath, name);
        if (!File.Exists(branchPath))
        {
            Console.WriteLine($"Branch {name} does not exist.");
            return;
        }
        File.WriteAllText(headPath, name);
        Console.WriteLine($"Switched to branch {name}");
    }

    public void Merge(string branchName)
    {
        var targetBranchPath = Path.Combine(branchesPath, branchName);
        if (!File.Exists(targetBranchPath))
        {
            Console.WriteLine($"Branch {branchName} does not exist.");
            return;
        }

        var currentBranch = File.ReadAllText(headPath).Trim();
        var currentBranchPath = Path.Combine(branchesPath, currentBranch);

        var currentBranchData = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(currentBranchPath));
        var targetBranchData = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(targetBranchPath));

        var currentHead = currentBranchData["head"].ToString();
        var targetHead = targetBranchData["head"].ToString();

        var currentCommit = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(Path.Combine(objectsPath, currentHead)));
        var targetCommit = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(Path.Combine(objectsPath, targetHead)));

        var currentChanges = JsonConvert.DeserializeObject<Dictionary<string, string>>(currentCommit["changes"].ToString());
        var targetChanges = JsonConvert.DeserializeObject<Dictionary<string, string>>(targetCommit["changes"].ToString());

        var mergeConflicts = new List<string>();
        var mergedChanges = new Dictionary<string, string>(currentChanges);

        foreach (var file in targetChanges)
        {
            if (!mergedChanges.ContainsKey(file.Key))
            {
                mergedChanges[file.Key] = file.Value;
            }
            else if (mergedChanges[file.Key] != file.Value)
            {
                mergeConflicts.Add(file.Key);
            }

            if (mergeConflicts.Count > 0)
            {
                Console.WriteLine("Merge conflicts detected");

                foreach (var conflict in mergeConflicts)
                {
                    Console.WriteLine($"Conflict: {conflict}");
                }
                Console.WriteLine("Merge aborted due to conflicts");
                return;
            }

            var mergeCommit = new Dictionary<string, object>
            {
                ["message"] = $"Merge branch {branchName} into {currentBranch}",
                ["changes"] = mergedChanges,
                ["parent"] = new List<string> { currentHead, targetHead }
            };

            var mergeCommitHash = HashObject(JsonConvert.SerializeObject(mergeCommit));
            currentBranchData["head"] = mergeCommitHash;
            ((List<string>)currentBranchData["history"]).Add(mergeCommitHash);
            File.WriteAllText(currentBranchPath, JsonConvert.SerializeObject(currentBranchData));

            Console.WriteLine($"Merge successful. Created merge commit {mergeCommitHash}");
        }
    }

    public void Status()
    {
        var index = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(indexPath));
        Console.WriteLine("Staged files:");
        foreach (var filepath in index.Keys)
        {
            Console.WriteLine($"  {filepath}");
        }
    }

    public void Ignore(string pattern)
    {
        File.AppendAllText(ignoreFile, $"{pattern}\n");
        Console.WriteLine($"Added {pattern} to .gitignore");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        string repoPath = "."; // Current directory as repository root
        MiniGitApp repo = new MiniGitApp(repoPath);

        Console.WriteLine("MiniGit Version Control System");
        Console.WriteLine("Available commands: init, add, commit, log, diff, branch, checkout, merge, status, ignore, exit");
        Console.WriteLine("Example usage: 'add file.txt', 'commit \"Message\"'");

        while (true)
        {
            Console.Write("> ");
            string input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;

            var parts = input.Split(' ', 2);
            var command = parts[0].ToLower();
            var argument = parts.Length > 1 ? parts[1] : null;

            try
            {
                switch (command)
                {
                    case "init":
                        repo.Initialize();
                        break;

                    case "add":
                        if (argument == null) Console.WriteLine("Usage: add <file>");
                        else repo.Add(argument);
                        break;

                    case "commit":
                        if (argument == null) Console.WriteLine("Usage: commit <message>");
                        else repo.Commit(argument);
                        break;

                    case "log":
                        repo.Log();
                        break;

                    case "diff":
                        var hashes = argument?.Split(' ');
                        if (hashes == null || hashes.Length != 2)
                        {
                            Console.WriteLine("Usage: diff <commitHash1> <commitHash2>");
                        }
                        else
                        {
                            repo.Diff(hashes[0], hashes[1]);
                        }
                        break;

                    case "branch":
                        if (argument == null) Console.WriteLine("Usage: branch <branchName>");
                        else repo.Branch(argument);
                        break;

                    case "checkout":
                        if (argument == null) Console.WriteLine("Usage: checkout <branchName>");
                        else repo.Checkout(argument);
                        break;

                    case "merge":
                        if (argument == null) Console.WriteLine("Usage: merge <branchName>");
                        else repo.Merge(argument);
                        break;

                    case "status":
                        repo.Status();
                        break;

                    case "ignore":
                        if (argument == null) Console.WriteLine("Usage: ignore <pattern>");
                        else repo.Ignore(argument);
                        break;

                    case "exit":
                        Console.WriteLine("Exiting MiniGit.");
                        return;

                    default:
                        Console.WriteLine("Unknown command. Type 'help' for a list of commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}

