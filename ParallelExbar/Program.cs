using CommandLine;
using System.Linq;
using System.Diagnostics;
using static ParallelExbar.MainProcedure;

namespace ParallelExbar
{
    public class Options
    {
        [Option('f', "file", Required = true, HelpText = "The name of a file including strings.")]
        public string? fileName { get; set; }

        [Option('i', "input", Required = true, HelpText = "Input format (abbadingo or json).")]
        public string? inputFormat { get; set; }
    }

    class Program
    {
        private static void print_DFA()
        {
            Console.WriteLine("States:"); 
            result.ToList().ForEach(n => Console.Write($" {n}"));
            Console.WriteLine("\nInitial: 0");
            Console.WriteLine("Finals:"); 
            result.Where(n => apta[n].label == TLabel.Accept)
                .ToList()
                .ForEach(n => Console.Write($" {n}"));
            Console.WriteLine("\nTransitions:");
            foreach (int n in result)
            {
                Console.Write($"{n}:"); 
                apta[n].children.ToList().ForEach(kv => Console.Write($" ({kv.Key}, {kv.Value})"));
                Console.WriteLine("");
            }
        }

        private static bool evalWordP(string word)
        {
            int state = 0;
            foreach (char c in word)
            {
                int next_state;
                if (apta[state].children.TryGetValue(c, out next_state))
                {
                    state = next_state;
                }
                else
                {
                    return false;
                }
            }
            return apta[state].label == TLabel.Accept;
        }

        static void Main(string[] args)
        {
            MPI.Environment.Run(ref args, comm =>
            {
                Stopwatch stopwatch = new Stopwatch();
                if (comm.Rank == 0)
                {
                    Parser.Default.ParseArguments<Options>(args).WithParsed(o =>
                    {
                        Console.WriteLine($"Loading data from {o.fileName}...");
                        InputData.readData(o.fileName, o.inputFormat);
                        Console.WriteLine("Broadcasting data and synthesizing...");
                    });
                }
                comm.Broadcast(ref InputData.alphabet, 0);
                comm.Broadcast(ref InputData.Splus, 0);
                comm.Broadcast(ref InputData.Sminus, 0);
                stopwatch.Start();
                exbar_main(comm);
                stopwatch.Stop();
                if (finished_successfully)
                {
                    print_DFA();
                    Console.WriteLine($"Done in {((double)stopwatch.ElapsedMilliseconds / 1000.0):0.00} seconds.");
                    Console.WriteLine($"The words in S_+ that are not accepted by automaton:");
                    foreach (string w in InputData.Splus)
                    {
                        if (!evalWordP(w))
                        {
                            Console.WriteLine(w.Length > 0 ? w : "@epsilon");
                        }
                    }
                    Console.WriteLine("The words in S_- that are accepted by automaton:");
                    foreach (string w in InputData.Sminus)
                    {
                        if (evalWordP(w))
                        {
                            Console.WriteLine(w.Length > 0 ? w : "@epsilon");
                        }
                    }
                    comm.Abort(0);
                }
            });
        }
    }
}

