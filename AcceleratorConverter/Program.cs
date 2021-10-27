using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using GlobExpressions;
using Newtonsoft.Json;
using TemplateEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AcceleratorConverter
{
    
    class Program
    {
        // const string ProjectName = "DotnetAccelerator";
        static void Main(string[] args)
        {
            string codeFolder = @"C:\Projects\DotnetAccelerator\";
            var reader = new JsonTextReader(new StringReader(File.ReadAllText(Path.Combine(codeFolder, @".template.config\template.json"))));
            var template = JsonSerializer.Create().Deserialize<Template>(reader);

            var accelerator = MapAcceleratorUI(template);

            var engine = MapEngine(template, codeFolder);

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithEventEmitter(x => new MultilineScalarFlowStyleEmitter(x))
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults | DefaultValuesHandling.OmitEmptyCollections)
                .Build();

            var acceleratorDefinition = new AcceleratorDefinition()
            {
                Accelerator = accelerator,
                Engine = engine
            };
            var yaml = serializer.Serialize(acceleratorDefinition);
            File.WriteAllText(Path.Combine(codeFolder, "accelerator.yaml"), yaml);
            Console.WriteLine(yaml);
        }

        private static Transform MapEngine(Template? template, string codeFolder)
        {
            var engine = new Combo()
            {
                Merge = new List<Transform>()
                {
                    new Combo()
                    {
                        Include = new List<string>() {"**"},
                        Exclude = new List<string>(){".template.config/**"}
                    }
                }
            };
            
            engine.Let = template.Symbols
                .Where(x => x.Value.Type == FluffyType.Computed)
                .Select(x => new Let() {Name = x.Key, Expression = ExpressionHelper.RewriteToSpEL(x.Value.Value)})
                .ToList();
            
            var globalExcludes = template.Sources
                .Where(x => x.Modifiers != null)
                .SelectMany(x => x.Modifiers
                    .Where(x => x.Condition == null && (x.Exclude?.Any() ?? false)))
                .SelectMany(x => x.Exclude)
                .ToList();


            var globalSearchReplace = template.Symbols
                .Where(x => !string.IsNullOrEmpty(x.Value.FileRename))
                .Select(x => (x.Value.FileRename, x.Key))
                .Union(new[] {(From: template.SourceName, To: "artifactId")})
                .ToDictionary(x => x.Item1, x => x.Item2);

            var replaceSymbols = globalSearchReplace.Keys.ToHashSet();

            // rewrite path file names with application name
            engine.Chain =  MyDirectory.GetGitTrackedFiles(codeFolder)
                .Select(x => Path.GetRelativePath(codeFolder, x).Replace(@"\","/"))
                .Where(x => replaceSymbols.Any(y => Regex.IsMatch(x,  y)))
                .Where(x => !globalExcludes.Any(excludePath => Glob.IsMatch(x, excludePath)))
                .Select(x =>
                {
                    var filePath = x;
                    foreach (var (search,replace) in globalSearchReplace)
                    {
                        filePath = $"{filePath.Replace(search, $"' + #{replace} + '")}";
                    }
                    return new RewritePath
                    {
                        Regex = x.Replace(@"\", "/"),
                        RewriteTo = $"'{filePath}'"
                    };
                })
                .Cast<Transform>()
                .ToList();
            // wildcard rename any text that uses projectname to actual application name

            engine.Chain.AddRange(globalSearchReplace.Select(x =>
            {
                var from = x.Key;
                var to = x.Value;
                return new ReplaceText()
                {
                    Substitutions = new List<Substitution>()
                    {
                        new Substitution()
                        {
                            Text = from,
                            With = $"#{to}"
                        }
                    }
                };
            }));
            // engine.Chain.Add(new ReplaceText()
            // {
            //     Substitutions = new List<Substitution>()
            //     {
            //         new Substitution()
            //         {
            //             Text = template.SourceName,
            //             With = "#artifactId"
            //         }
            //     }
            // });
            

            AddConditionalFiles(template, engine);

            var templatedFiles = GetTemplatedFiles(template, codeFolder);


            var templatesByFile = templatedFiles
                .SelectMany(x => x.Value.Select(match => new
                {
                    File = x.Key,
                    Condition = match.Groups["expression"].Value,
                    Block = match.Value
                }))
                .GroupBy(x => (x.File))
                .ToList();
            foreach (var fileGroup in templatesByFile)
            {
                var fileName = fileGroup.Key;
                var fileMerge = new Combo()
                {
                    Include = new List<string>() {fileName.Replace(@"\", @"/")},
                    Chain = new()
                };
                engine.Merge.Add(fileMerge);
                foreach (var symbolGroup in fileGroup.GroupBy(x => x.Condition))
                {
                    var condition = symbolGroup.Key;
                    fileMerge.Chain.Add(new ReplaceText()
                    {
                        Condition = ExpressionHelper.RewriteToSpEL(ExpressionHelper.Invert(condition)),
                        Substitutions = symbolGroup.DistinctBy(x => x.Block).Select(x => new Substitution() {Text = x.Block, With = "''"}).ToList()
                    });
                    fileMerge.Chain.Add(new ReplaceText
                    {
                        Condition = ExpressionHelper.RewriteToSpEL(condition),
                        Substitutions = symbolGroup
                            .DistinctBy(x => x.Block)
                            .Select(x =>
                        {
                            var blockLines = x.Block.Trim().Split("\n");
                            var rewrite = string.Join("\n", blockLines[1..^1]);
                            rewrite = rewrite.Replace("'", "''");
                            return new Substitution()
                            {
                                Text = x.Block,
                                With = $"'{rewrite}'"
                            };
                        }).ToList()
                    });
                }
            }
            
            

            return engine;
        }

        private static void AddConditionalFiles(Template? template, Combo engine)
        {
            // .net does optional files via exclusion. this approach doesn't work well with accelerator merge
            // we first exclude the file under default selection criteria, and optionally ADD it in if the condition is true
            // condition therefore has to be inverted from what it isn template.json
            if (template.Sources == null)
                return;
            
            
            var conditionalExcludes = template.Sources
                .Where(x => x.Modifiers != null)
                .SelectMany(x => x.Modifiers
                    .Where(x =>  (x.Include == null || !x.Include.Any())))
                .GroupBy(x => x.Condition)
                .ToList();

            var globalMatch = (Combo)engine.Merge.First();
            foreach (var conditionalFile in conditionalExcludes)
            {
                var condition = !string.IsNullOrEmpty(conditionalFile.Key) ? ExpressionHelper.RewriteToSpEL(ExpressionHelper.Invert(conditionalFile.Key)) : null;
                var include = conditionalFile.SelectMany(x => x.Exclude.AsEnumerable()).ToList();
                if (condition != null)
                {
                    var merge = new Combo
                    {
                        Condition = condition,
                        Include = include,
                    };
                    engine.Merge.Add(merge);
                }

                // we add any files that are conditional to global exclusion list and then selectively add them back with condition
                globalMatch.Exclude.AddRange(include);
            }
        }
        
        private static List<string> GetKnownSymbols(Template template) => template.Symbols.Select(x => x.Key).ToList();

        private static Dictionary<string, MatchCollection> GetTemplatedFiles(Template? template, string codeFolder)
        {
            var knownSymbols = GetKnownSymbols(template);
            var regexOptions = RegexOptions.Singleline | RegexOptions.Compiled;
            var hashCommentPattern = new Regex($"[ ]*#if (?<expression>.+?)\n.+?#endif[ ]*", regexOptions);
            var jsonPattern = new Regex($"[ ]*//#if (?<expression>.+?)\n.+?//#endif[ ]*", regexOptions);
            var xmlPattern = new Regex($@"[ ]*<!--\s*#if (?<expression>.+?)-->.+?<!--\s*#endif\s*-->", regexOptions);
            Dictionary<string, MatchCollection> templatedFiles = new();
            var files = MyDirectory.GetFiles(codeFolder, new[]{"*.cs","*.csproj", "*.yaml", "*.json"}, SearchOption.AllDirectories)
                .Where(x =>
                {
                    var filename = Path.GetFileName(x);
                    return !(filename.StartsWith("accelerator") && filename.EndsWith(".yaml"));
                });

            bool ReferencesTemplateSymbols(Match match)
            {
                var expression = match.Groups["expression"].Value;
                return ExpressionHelper.ContainsSymbols(expression, knownSymbols);
            }
            foreach (var file in files)
            {
                var fileText = File.ReadAllText(file).Replace("\r\n","\n");
                var extension = new FileInfo(file).Extension;
                var pattern = extension switch
                {
                    ".cs" or ".yaml" => hashCommentPattern,
                    ".csproj" or ".xml" => xmlPattern,
                    ".json" => jsonPattern
                };
                var matches = pattern.Matches(fileText);
                
                if (matches.Where(ReferencesTemplateSymbols).Any())
                {
                    templatedFiles.Add(Path.GetRelativePath(codeFolder, file), matches);
                }
            }

            return templatedFiles;
        }
        

        private static Accelerator MapAcceleratorUI(Template? template)
        {
            var accelerator = new Accelerator();
            accelerator.DisplayName = template.Name;
            accelerator.Description = template.Description;
            accelerator.Tags = template.Tags.Select(x => x.Value).ToList();
            accelerator.IconUrl = "https://iconape.com/wp-content/files/km/370669/svg/370669.svg";
            accelerator.Options = new();

            // var projectNameOption = new Option
            // {
            //     Name = "artifactId",
            //     DataType = "string",
            //     Label = "Name",
            //     Required = true,
            //     InputType = InputType.Text.ToString().ToLower(),
            //     DefaultValue = template.SourceName
            // };
            // accelerator.Options.Add(projectNameOption);
            
            foreach (var (symbolName, symbolDefinition) in template.Symbols.Where(x => x.Value.Type == FluffyType.Parameter))
            {

                var option = new Option()
                {
                    Name = symbolName,
                    Label = symbolDefinition.Label,
                    Description = symbolDefinition.Description,
                    DataType = symbolDefinition.Datatype switch
                    {
                        Datatype.Bool => "boolean",
                        _ => "string"
                    },
                    InputType = (symbolDefinition.InputType ?? symbolDefinition.Datatype switch
                    {
                        Datatype.Choice => InputType.Radio,
                        Datatype.Bool => InputType.Toggle,
                        _ => symbolDefinition.Choices?.Any() ?? false ? InputType.Radio : InputType.Text
                    }).ToString().ToLower(),
                    DefaultValue = symbolDefinition.DefaultValue,
                    Choices = symbolDefinition.Choices?
                        .Where(x => x != null)
                        .Select(x => new Choice {Text = x.Description, Value = x.Choice})
                        .ToList(),
                    Required = symbolDefinition.IsRequired
                };
                accelerator.Options.Add(option);
            }

            return accelerator;
        }

        private AcceleratorDefinition ReadAcceleratorFile(string file)
        {
            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();
            var yaml = File.ReadAllText(@"C:\Projects\DotnetAccelerator\accelerator.yaml");
            var definition = deserializer.Deserialize<AcceleratorDefinition>(yaml);
            return definition;
        }
    }
}