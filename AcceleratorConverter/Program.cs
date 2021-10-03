using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TemplateEngine;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AcceleratorConverter
{
    class Program
    {
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
            var engine = new Engine()
            {
                Merge = new List<Merge>()
                {
                    new Merge()
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

            AddConditionalFiles(template, engine);

            var templatedFiles = GetTemplatedFiles(template, codeFolder);


            var templatesByFileAndCondition = templatedFiles
                .SelectMany(x => x.Value.Select(match => new
                {
                    File = x.Key,
                    Condition = match.Groups["expression"].Value,
                    Block = match.Value
                }))
                .GroupBy(x => (x.File))
                .ToList();
            foreach (var fileGroup in templatesByFileAndCondition)
            {
                var fileName = fileGroup.Key;
                var fileMerge = new Merge()
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

        private static void AddConditionalFiles(Template? template, Engine engine)
        {
            // .net does optional files via exclusion. this approach doesn't work well with accelerator merge
            // we first exclude the file under default selection criteria, and optionally ADD it in if the condition is true
            // condition therefore has to be inverted from what it isn template.json
            if (template.Sources == null)
                return;
            
            var conditionalExcludes = template.Sources
                .Where(x => x.Modifiers != null)
                .SelectMany(x => x.Modifiers
                    .Where(x => x.Condition != null && (x.Include == null || !x.Include.Any())))
                .GroupBy(x => x.Condition)
                .ToList();

            var globalMatch = engine.Merge.First();
            foreach (var conditionalFile in conditionalExcludes)
            {
                var condition = ExpressionHelper.RewriteToSpEL(ExpressionHelper.Invert(conditionalFile.Key));
                var merge = new Merge
                {
                    Condition = condition,
                    Include = conditionalFile.SelectMany(x => x.Exclude.AsEnumerable()).ToList(),
                };
                engine.Merge.Add(merge);
                globalMatch.Exclude.AddRange(merge.Include);
            }
        }

        private static Dictionary<string, MatchCollection> GetTemplatedFiles(Template? template, string codeFolder)
        {
            var knownSymbols = template.Symbols.Select(x => x.Key).ToList();
            var regexOptions = RegexOptions.Singleline | RegexOptions.Compiled;
            var hashCommentPattern = new Regex($"[ ]*#if (?<expression>.+?)\n.+?#endif[ ]*\n?", regexOptions);
            var jsonPattern = new Regex($"[ ]*//#if (?<expression>.+?)\n.+?//#endif[ ]*\n?", regexOptions);
            var xmlPattern = new Regex($@"[ ]*<!--\s*#if (?<expression>.+?)-->.+?<!--\s*#endif\s*-->", regexOptions);
            Dictionary<string, MatchCollection> templatedFiles = new();
            var files = MyDirectory.GetFiles(codeFolder, new[]{"*.cs","*.csproj", "*.yaml", "*.json"}, SearchOption.AllDirectories)
                .Where(x => !Path.GetFileName(x).Equals("accelerator.yaml"));

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
            accelerator.Options = new()
            {
                new Option()
                {
                    Name = "applicationName",
                    Label = "Application Name",
                    Description = "Application Name",
                    InputType = InputType.Text.ToString().ToLower(),
                    DefaultValue = template.SourceName,
                    Required = true,
                }
            };
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
                        _ => symbolDefinition.Choices.Any() ? InputType.Radio : InputType.Text
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