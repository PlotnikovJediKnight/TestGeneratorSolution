using System.IO;
using System;

namespace FileInputOutputProject.TestFiles
{
    class MainClass
    {
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            int maxParallelism = 5;
            string[] files = Directory.GetFiles("TestFiles", "*.cs");
            var configuration = new PipelineConfiguration(maxReadingTasks: maxParallelism,
                maxProcessingTasks: maxParallelism,
                maxWritingTasks: maxParallelism);

            var pipeline = new Pipeline(configuration);
            await pipeline.PerformProcessing(files);
            Console.ReadKey();
        }
    }
}
