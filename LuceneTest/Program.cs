using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Ru;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Serilog;
using Directory = Lucene.Net.Store.Directory;
using Version = Lucene.Net.Util.Version;

namespace LuceneTest
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            ConfigureLogger();
            var filePath = args.Length > 0 ? args[0] : null;
            if (string.IsNullOrEmpty(filePath))
            {
                Log.Logger.Error("Не указан текстовый файл.");
                return 1;
            }

            if (File.Exists(filePath) == false)
            {
                Log.Logger.Error("Файл '{File}' не найден.", filePath);
                return 1;
            }

            using (var directory = DirectoryFactory.CreateDirectory())
            using (var analyzer = AnalyzerFactory.CreateAnalyzer())
            {
                WriteData(filePath, directory, analyzer);

                while (true)
                {
                    Console.WriteLine("Введите искомую строку");
                    var searchText = Console.ReadLine();
                    if(string.Equals("exit", searchText, StringComparison.OrdinalIgnoreCase)) break;
                    var query = QueryFactory.Create(analyzer, searchText);

                    using (var searcher = new IndexSearcher(directory))
                    {
                        var sort = new Sort(SortField.FIELD_SCORE, new SortField("id", SortField.STRING));
                        var docs = searcher.Search(query, 100 /*, sort*/);
                        //                    var docs = searcher.Search(booleanQuery, 100);
                        Log.Logger.Information("Найдено {TotalHits} совпадений.", docs.TotalHits);
                        Log.Logger.Information("Наибольшее значение совпадения: {MaxScore}.", docs.MaxScore);
                        foreach (var docsScoreDoc in docs.ScoreDocs)
                        {
                            var doc = searcher.Doc(docsScoreDoc.Doc);
                            var id = doc.Get("id");
                            var score = docsScoreDoc.Score;
                            Log.Logger.Information("Строка {id}: {score}", id, score);
                        }
                    }
                    
                }
            }


            return 0;
        }


        private static void ConfigureLogger()
        {
            var logConfig = new LoggerConfiguration();
            logConfig.MinimumLevel.Verbose().WriteTo.Console();
            Log.Logger = logConfig.CreateLogger();
        }


        private static void WriteData(string filePath, Directory directory, Analyzer analyzer)
        {
            using (var indexWriter = new IndexWriter(directory, analyzer, IndexWriter.MaxFieldLength.UNLIMITED))
            {
                foreach (var line in File.ReadAllLines(filePath)
                    .Select((line, lineNum) => new {text = line, lineNum = lineNum + 1}))
                {
                    if (string.IsNullOrWhiteSpace(line.text)) continue;
                    var doc = DocumentFactory.Create(line.lineNum, line.text);
                    indexWriter.AddDocument(doc);
                }

                indexWriter.Flush(true, true, false);
            }
        }
    }


    internal static class DocumentFactory
    {
        private static readonly NumericField IdField = new NumericField("id", Field.Store.YES, false);

        public static Document Create(int id, string text)
        {
            var document = new Document();
            document.Add(new Field("id", $"{id}", Field.Store.YES, Field.Index.NO));
            document.Add(new Field("text", text, Field.Store.NO, Field.Index.ANALYZED));
            return document;
        }
    }

    internal static class AnalyzerFactory
    {
        public static Analyzer CreateAnalyzer()
        {
            return new RussianAnalyzer(Version.LUCENE_30);
        }
    }

    internal static class DirectoryFactory
    {
        public static Directory CreateDirectory()
        {
            return new RAMDirectory();
        }
    }

    internal static class QueryFactory
    {
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static Query Create(Analyzer analyzer, string searchText)
        {
            if (analyzer == null) throw new ArgumentNullException(nameof(analyzer));
            var queryParser = new QueryParser(Version.LUCENE_30, "text", analyzer);
            var slop = SplitTextToWords(searchText).Length;
            var phraseQuery = new PhraseQuery {Slop = slop > 0 ? slop : 1};

            foreach (var word in SplitTextToWords(searchText)) phraseQuery.Add(new Term("text", word));

            var booleanQuery =
                new BooleanQuery {{phraseQuery, Occur.SHOULD}, {queryParser.Parse(searchText), Occur.SHOULD}};
            return booleanQuery;
            //            return phraseQuery;
            //            return queryParser.Parse(searchText);
        }

        private static string[] SplitTextToWords(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return WhitespaceRegex.Split(text);
        }
    }
}