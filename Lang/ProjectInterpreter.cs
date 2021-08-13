using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lang
{
    public interface IProjectInterpreter
    {
        List<string> LoadProject(string projectPath);
    }

    public class ProjectInterpreter : IProjectInterpreter
    {
        private const string ProjectFileExtension = ".olproj";
        private const string ProjectFilePattern = "*.olproj";
        private const string SourceFilePattern = "*.ol";

        private enum ProjectFileSection
        {
            None,
            Name,
            Dependencies,
            Linker,
            Exclude
        }

        public List<string> LoadProject(string projectPath)
        {
            // 1. Check if project file is null or a directory
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = GetProjectPathInDirectory(Directory.GetCurrentDirectory());
            }
            else if (!projectPath.EndsWith(ProjectFileExtension))
            {
                projectPath = GetProjectPathInDirectory(projectPath);
            }

            // 2. Load the project file
            BuildSettings.Path = Path.GetDirectoryName(Path.GetFullPath(projectPath));

            var excludedFiles = new List<string>();
            var currentSection = ProjectFileSection.None;
            foreach (var line in File.ReadLines(projectPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    currentSection = ProjectFileSection.None;
                }
                else if (currentSection != ProjectFileSection.None)
                {
                    switch (currentSection)
                    {
                        case ProjectFileSection.Name:
                            BuildSettings.Name = line;
                            break;
                        case ProjectFileSection.Dependencies:
                            BuildSettings.Dependencies.Add(line);
                            break;
                        case ProjectFileSection.Linker:
                            BuildSettings.Linker = (LinkerType) Enum.Parse(typeof(LinkerType), line, true);
                            break;
                        case ProjectFileSection.Exclude:
                            excludedFiles.Add(Path.Combine(BuildSettings.Path, line));
                            break;
                    }
                }
                else
                {
                    currentSection = line switch
                    {
                        "#name" => ProjectFileSection.Name,
                        "#dependencies" => ProjectFileSection.Dependencies,
                        "#linker" => ProjectFileSection.Linker,
                        "#exclude" => ProjectFileSection.Exclude,
                        _ => ProjectFileSection.None,
                    };
                }
            }

            // 3. Recurse through the directories and load the files to build
            var sourceFiles = GetSourceFiles(new DirectoryInfo(BuildSettings.Path), excludedFiles).ToList();

            // 4. Load runtime and dependency files
            var libraryDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Runtime");
            var libraryFiles = GetSourceFiles(new DirectoryInfo(libraryDirectory));
            sourceFiles.AddRange(libraryFiles);

            return sourceFiles;
        }

        private static string GetProjectPathInDirectory(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Console.WriteLine($"Path \"{directory}\" does not exist");
                Environment.Exit(ErrorCodes.ProjectFileNotFound);
            }

            // 1. Search for an project file in the current directory
            var projectPath = Directory.EnumerateFiles(directory, ProjectFilePattern)
                .FirstOrDefault();

            // 2. If no project file, throw and exit
            if (projectPath == null)
            {
                Console.WriteLine($"Project file not found in directory: \"{directory}\"");
                Environment.Exit(ErrorCodes.ProjectFileNotFound);
            }

            return projectPath;
        }

        private static IEnumerable<string> GetSourceFiles(DirectoryInfo directory, List<string> excluded = null)
        {
            if (excluded?.Contains(directory.FullName) ?? false)
            {
                yield break;
            }

            foreach (var sourceFile in directory.GetFiles(SourceFilePattern))
            {
                yield return sourceFile.FullName;
            }

            foreach (var subDirectory in directory.GetDirectories())
            {
                if (subDirectory.Name == "bin" || subDirectory.Name == "obj")
                    continue;

                foreach (var sourceFile in GetSourceFiles(subDirectory, excluded))
                {
                    yield return sourceFile;
                }
            }
        }
    }
}
