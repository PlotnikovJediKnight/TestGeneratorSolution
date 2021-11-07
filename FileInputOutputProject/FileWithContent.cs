using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileInputOutputProject
{
    class FileWithContent
    {
        public string Path { get; }

        public string Content { get; }

        public FileWithContent(string path, string content)
        {
            Path = path;
            Content = content;
        }
    }
}
