using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GlobExpressions;
using Ignore;

namespace AcceleratorConverter
{
    public static class MyDirectory
    {   // Regex version
        public static IEnumerable<string> GetGitTrackedFiles(string path)
        {
            var skipLine = new Regex(@"^\s*#");
            var gitIgnoreText = File.ReadAllText(Path.Combine(path, ".gitignore"))
                .Split("\n")
                .Where(x => !skipLine.IsMatch(x));
            var allFiles = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories);
            var ignore = new Ignore.Ignore();
            ignore.Add(gitIgnoreText);
            ignore.Add(".git/**");
            foreach (var file in allFiles.Where(x => !ignore.IsIgnored(Path.GetRelativePath(path, x).Replace(@"\","/"))))
            {
                yield return file;
            }
        }
        public static IEnumerable<string> GetFiles(string path, 
            string searchPatternExpression = "",
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            Regex reSearchPattern = new Regex(searchPatternExpression, RegexOptions.IgnoreCase);
            return Directory.EnumerateFiles(path, "*", searchOption)
                .Where(file =>
                    reSearchPattern.IsMatch(file));
        }

        // Takes same patterns, and executes in parallel
        public static IEnumerable<string> GetFiles(string path, 
            string[] searchPatterns, 
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return searchPatterns.AsParallel()
                .SelectMany(searchPattern => 
                    Directory.EnumerateFiles(path, searchPattern, searchOption));
        }
    }
}