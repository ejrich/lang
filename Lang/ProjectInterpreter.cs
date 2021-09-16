using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lang
{
    public class ProjectFile
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public Linker Linker { get; set; }
        public List<string> SourceFiles { get; set; }
        public HashSet<string> Dependencies { get; } = new();
        public List<string> Packages { get; } = new();
        public List<string> Exclude { get; } = new();
    }

    public enum Linker
    {
        Static,
        Dynamic
    }

    public interface IProjectInterpreter
    {
        ProjectFile LoadProject(string projectPath);
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
            Packages,
            Linker,
            Exclude
        }

        public ProjectFile LoadProject(string projectPath)
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
            var projectFile = LoadProjectFile(projectPath);

            // 3. Recurse through the directories and load the files to build
            projectFile.SourceFiles = GetSourceFiles(new DirectoryInfo(projectFile.Path), projectFile.Exclude).ToList();

            // 4. Load runtime and dependency files
            var libraryDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Runtime");
            var libraryFiles = GetSourceFiles(new DirectoryInfo(libraryDirectory));
            projectFile.SourceFiles.AddRange(libraryFiles);

            return projectFile;
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

        private static ProjectFile LoadProjectFile(string projectPath)
        {
            var projectFile = new ProjectFile
            {
                Path = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            };

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
                            projectFile.Name = line;
                            break;
                        case ProjectFileSection.Dependencies:
                            projectFile.Dependencies.Add(line);
                            break;
                        case ProjectFileSection.Packages:
                            projectFile.Packages.Add(line);
                            break;
                        case ProjectFileSection.Linker:
                            projectFile.Linker = (Linker) Enum.Parse(typeof(Linker), line, true);
                            break;
                        case ProjectFileSection.Exclude:
                            projectFile.Exclude.Add(Path.Combine(projectFile.Path, line));
                            break;
                    }
                }
                else
                {
                    currentSection = line switch
                    {
                        "#name" => ProjectFileSection.Name,
                        "#dependencies" => ProjectFileSection.Dependencies,
                        "#packages" => ProjectFileSection.Packages,
                        "#linker" => ProjectFileSection.Linker,
                        "#exclude" => ProjectFileSection.Exclude,
                        _ => ProjectFileSection.None,
                    };
                }
            }

            return projectFile;
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
