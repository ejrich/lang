using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lang.Project
{
    public interface IProjectInterpreter
    {
        Project LoadProject(string projectPath);
    }

    public class ProjectInterpreter : IProjectInterpreter
    {
        private const string ProjectFileExtension = ".olproj";
        private const string SourceFileExtension = ".ol";

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
            var projectFileContents = File.ReadAllText(projectPath);

            // 3. Parse project file
            // @PerformanceCheck - this takes about 40x longer than the rest of the steps ~130 ms
            var projectFile = JsonSerializer.Deserialize<ProjectFile>(projectFileContents,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            // 4. Recurse through the directories and load the files to build
            var sourceFiles = GetSourceFiles(new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))));

            return new Project
            {
                Name = projectFile.Name,
                BuildFiles = sourceFiles.ToList()
            };
        }

        private static string GetProjectPathInDirectory(string directory)
        {
            // a. Search for an project file in the current directory
            var projectPath = Directory.EnumerateFiles(directory)
                .FirstOrDefault(_ => _.EndsWith(ProjectFileExtension));

            // b. If no project file, throw and exit
            if (projectPath == null)
            {
                Console.WriteLine($"Project file not found in directory: \"{directory}\"");
                Environment.Exit(ErrorCodes.ProjectFileNotFound);
            }

            return projectPath;
        }

        private static IEnumerable<string> GetSourceFiles(DirectoryInfo directory)
        {
            foreach (var sourceFile in directory.GetFiles().Where(_ => _.Name.EndsWith(SourceFileExtension)))
            {
                yield return sourceFile.FullName;
            }

            foreach (var subDirectory in directory.GetDirectories().Where(_ => _.Name != "bin"))
            {
                foreach (var sourceFile in GetSourceFiles(subDirectory))
                {
                    yield return sourceFile;
                }
            }
        }
    }
}
