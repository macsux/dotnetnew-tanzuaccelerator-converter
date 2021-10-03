using System.Collections.Generic;

namespace AcceleratorConverter
{
    public abstract class Transform
    {
        public abstract string Type { get; }
        public string Condition { get; set; }
        
    }

    public class ReplaceText : Transform
    {
        public override string Type => "ReplaceText";
        public List<Substitution> Substitutions { get; set; }
    }
    
    public class Combo : Transform
    {
        public override string Type => "Combo";
        public List<Let> Let { get; set; }
        public List<string> Include { get; set; }
        public List<string> Exclude { get; set; }
        public List<Transform> Merge { get; set; }
        public List<Transform> Chain { get; set; }
        public ConflictResolutionStrategy OnConflict { get; set; }
    }

    public enum ConflictResolutionStrategy
    {
        /// <summary>
        ///  stop processing on the first file that exhibits path conflicts.
        /// </summary>
        Fail,
        /// <summary>
        /// for each conflicting file, the file produced first (typically by a transform appearing earlier in the yaml definition) is retained
        /// </summary>
        UseFirst,
        /// <summary>
        /// for each conflicting file, the file produced last (typically by a transform appearing later in the yaml definition) is retained
        /// </summary>
        UseLast,
        /// <summary>
        /// the conflicting versions of files are concatenated (as if using cat file1 file2 ...), with files produced first appearing first.
        /// </summary>
        Append 
    }
}