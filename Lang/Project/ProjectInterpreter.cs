using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lang.Project
{
    public interface IProjectInterpreter
    {
        Project LoadProject(string projectPath);
    }

    public class ProjectInterpreter : IProjectInterpreter
    {
        private const string ProjectFileExtension = ".olproj";
        private const string ProjectFilePattern = "*.olproj";
        private const string SourceFilePattern = "*.ol";

        public Project LoadProject(string projectPath)
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
            var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
            var sourceFiles = GetSourceFiles(new DirectoryInfo(projectDirectory));

            return new Project
            {
                Name = projectFile.Name,
                Path = projectDirectory,
                BuildFiles = sourceFiles.ToList()
            };
        }

        private static string GetProjectPathInDirectory(string directory)
        {
            // a. Search for an project file in the current directory
            var projectPath = Directory.EnumerateFiles(directory, ProjectFilePattern)
                .FirstOrDefault();

            // b. If no project file, throw and exit
            if (projectPath == null)
            {
                Console.WriteLine($"Project file not found in directory: \"{directory}\"");
                Environment.Exit(ErrorCodes.ProjectFileNotFound);
            }

            return projectPath;
        }

        private static ProjectFile LoadProjectFile(string projectPath)
        {
            var projectFile = new ProjectFile();

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
                    }
                }
                else
                {
                    currentSection = line switch
                    {
                        "#name" => ProjectFileSection.Name,
                        "#dependencies" => ProjectFileSection.Dependencies,
                        "#packages" => ProjectFileSection.Packages,
                        _ => ProjectFileSection.None,
                    };
                }
            }

            return projectFile;
        }

        private static IEnumerable<string> GetSourceFiles(DirectoryInfo directory)
        {
            foreach (var sourceFile in directory.GetFiles(SourceFilePattern))
            {
                yield return sourceFile.FullName;
            }

            foreach (var subDirectory in directory.GetDirectories())
            {
                if (subDirectory.Name == "bin" || subDirectory.Name == "obj")
                    continue;

                foreach (var sourceFile in GetSourceFiles(subDirectory))
                {
                    yield return sourceFile;
                }
            }
        }
    }
}
