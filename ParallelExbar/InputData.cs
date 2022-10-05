using static System.Environment;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace ParallelExbar
{
    public static class Extensions
    {
        public static void Deconstruct<T>(this IList<T> list, out T first, out T second)
        {
            first = list.Count > 0 ? list[0] : default(T); // or throw
            second = list.Count > 1 ? list[1] : default(T); // or throw
        }

        public static void Deconstruct<T>(this IList<T> list, out T first, out T second, out T third)
        {
            first = list.Count > 0 ? list[0] : default(T); // or throw
            second = list.Count > 1 ? list[1] : default(T); // or throw
            third = list.Count > 2 ? list[2] : default(T); // or throw
        }
    }

    public static class InputData
    {
        private class JsonProblemInstance
        {
            public DateTime dateCreated { get; set; }
            public string docType { get; set; }
            public string[] negative { get; set; }
            public int numNegative { get; set; }
            public int numPositive { get; set; }
            public int numTotal { get; set; }
            public string[] positive { get; set; }
            public int version { get; set; }
        }

        public static List<string> Splus = new();
        public static List<string> Sminus = new();
        public static string alphabet = "";

        public static IList<string> decode(string line, string pattern, params int[] groupInds)
        {
            Regex parts = new Regex(pattern);
            Match match = parts.Match(line);
            List<string> result = new();
            if (match.Success)
            {
                var groups = match.Groups;
                foreach (int i in groupInds)
                {
                    result.Add(groups[i].Value);
                }
            }
            else
            {
                Console.WriteLine($"{line} does not match to {pattern}");
                Exit(1);
            }
            return result;
        }

        private static void readAbbadingo(string fileName)
        {
            string[] lines = Array.Empty<string>();
            try
            {
                lines = File.ReadAllLines(fileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"There is a problem with file {fileName}:");
                Console.WriteLine($"{ex.GetType()} says {ex.Message}");
                Exit(1);
            }
            int n = lines.Length;
            if (n <= 1)
            {
                Console.WriteLine($"There are no words in file {fileName}.");
                Exit(1);
            }
            (string sampleSize, string alphabetSize) =
                decode(lines[0], @"^(\d+)\s+(\d+)\s*$", 1, 2);
            if (n - 1 != int.Parse(sampleSize))
            {
                Console.WriteLine("There are more or less strings than declared.");
                Exit(1);
            }
            List<string> examples = new();
            List<string> counterexamples = new();
            for (int i = 1; i < n; i++)
            {
                var (label, len, seq) =
                    decode(lines[i], @"^(\d)\s+(\d+)\s*((\s\w)*)\s*$", 1, 2, 3);
                string word = seq.Replace(" ", "");
                if (word.Length != int.Parse(len))
                {
                    Console.WriteLine($"The length of {i}th word is not correct.");
                    Exit(1);
                }
                if (label == "1")
                {
                    examples.Add(word);
                }
                else if (label == "0")
                {
                    counterexamples.Add(word);
                }
                else
                {
                    Console.WriteLine($"Unrecognized label in line {i}.");
                    Exit(1);
                }
            }
            Splus = examples.ToList();
            Sminus = counterexamples.ToList();
            if (examples.Intersect(counterexamples).Count() > 0)
            {
                Console.WriteLine("Examples and counterexamples have nonempty intersection.");
                Exit(1);
            }
            alphabet = "";
            foreach (string w in Splus.Union(Sminus))
            {
                foreach (char symbol in w)
                {
                    if (!alphabet.Contains(symbol))
                    {
                        alphabet += symbol;
                    }
                }
            }
            if (alphabet.Length > int.Parse(alphabetSize))
            {
                Console.WriteLine("Declared alphabet size is less than real alphabet size.");
                Exit(1);
            }
        }

        private static void readJson(string fileName)
        {
            if (!Regex.IsMatch(fileName, @"\w+_(Target){0,1}(Test){0,1}(Train){0,1}.json"))
            {
                Console.WriteLine($"File name: {fileName} is not problem instance");
                Exit(1);
            }

            var json = File.ReadAllText(fileName);

            JsonProblemInstance? problemInstance = JsonConvert.DeserializeObject<JsonProblemInstance>(json);
            if (problemInstance == null)
            {
                Console.WriteLine($"Cannot deserialize problem instance in {fileName}");
                Exit(1);
            }

            if (problemInstance.negative?.Length != problemInstance.numNegative)
            {
                Console.WriteLine($"Negative words count discrepency found in {fileName}. Should be {problemInstance.numNegative} has {problemInstance.negative?.Length}");
                Exit(1);
            }

            if (problemInstance.positive?.Length != problemInstance.numPositive)
            {
                Console.WriteLine($"Positive words count discrepency found in {fileName}. Should be {problemInstance.numPositive} has {problemInstance.positive?.Length}");
                Exit(1);
            }

            if (problemInstance.numTotal != problemInstance.numNegative + problemInstance.numPositive)
            {
                Console.WriteLine($"Positive words count and negative words discrepency found in {fileName}. Should be {problemInstance.numTotal} has {problemInstance.numNegative + problemInstance.numPositive}");
                Exit(1);
            }

            if (problemInstance.positive.Intersect(problemInstance.negative).Count() > 0)
            {
                Console.WriteLine("Examples and counterexamples have nonempty intersection.");
                Exit(1);
            }

            HashSet<char> alphabet = new();

            foreach (var word in Enumerable.Concat(problemInstance.positive, problemInstance.negative))
            {
                Array.ForEach(word.ToCharArray(), c => alphabet.Add(c));
            }

            var sortedAlphabet = alphabet.ToList();
            sortedAlphabet.Sort();

            InputData.alphabet = String.Join("", sortedAlphabet);
            InputData.Splus = problemInstance.positive.ToList();
            InputData.Sminus = problemInstance.negative.ToList();
        }

        public static void readData(string fileName, string inputFormat)
        {
            if (inputFormat == "abbadingo")
            {
                readAbbadingo(fileName);
            }
            else
            {
                readJson(fileName);
            }
        }
    }
}
