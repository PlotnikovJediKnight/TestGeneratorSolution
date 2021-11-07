using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace FileInputOutputProject
{
    class TestClassGenerator
    {
        public FileWithContent[] GetTestClassFiles(FileWithContent a)
        {
            return new FileWithContent[1] { new FileWithContent(a.Path + ".processed", a.Content.ToUpper())};
        }
    }

    public class Pipeline
    {
        private readonly PipelineConfiguration _pipelineConfiguration;
        private readonly TestClassGenerator _generator;

        public Pipeline(PipelineConfiguration pipelineConfiguration)
        {
            _pipelineConfiguration = pipelineConfiguration;
            _generator = new TestClassGenerator();
        }

        public async Task PerformProcessing(IEnumerable<string> files)
        {

            var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };

            var readingBlock = new TransformBlock<string, FileWithContent>(
                async path => new FileWithContent(path, await ReadFile(path)),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = _pipelineConfiguration.MaxReadingTasks });

            var processingBlock = new TransformBlock<FileWithContent, FileWithContent[]>(
                fwc => _generator.GetTestClassFiles(fwc),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = _pipelineConfiguration.MaxProcessingTasks });

            var writingBlock = new ActionBlock<FileWithContent[]>(async fwc => await WriteFile(fwc),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = _pipelineConfiguration.MaxWritingTasks });

            readingBlock.LinkTo(processingBlock, linkOptions);
            processingBlock.LinkTo(writingBlock, linkOptions);

            foreach (string file in files)
            {
                readingBlock.Post(file);
            }

            readingBlock.Complete();

            await writingBlock.Completion;
        }

        private async Task<string> ReadFile(string filePath)
        {
            string result;
            using (var streamReader = new StreamReader(filePath))
            {
                result = await streamReader.ReadToEndAsync();
            }
            return result;
        }

        private string ProcessFile(string fileContent)
        {
            string result = fileContent.ToUpper();
            Thread.Sleep(250);
            return result;
        }

        private async Task WriteFile(FileWithContent[] filesWithContent)
        {
            using (var streamWriter = new StreamWriter(filesWithContent[0].Path))
            {
                await streamWriter.WriteAsync(filesWithContent[0].Content);
            }
        }
    }
}
