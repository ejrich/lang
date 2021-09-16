using System.IO;
using System.Linq;

namespace Lang.Project
{
    public interface IProjectInterpreter
    {
        Project LoadProject();
    }

    public class ProjectInterpreter : IProjectInterpreter
    {
        public Project LoadProject()
        {
            var project = new Project
            {
                Name = "Test",
                BuildFiles = Directory.GetFiles(Directory.GetCurrentDirectory()).ToList() // TODO make this not directory
            };

            return project;
        }
    }
}
