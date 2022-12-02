using static System.Environment;
using System.Collections.Immutable;

namespace ParallelExbar
{
    // Based on Lang, K.J.: Faster algorithms for finding minimal
    // consistent DFAs. Technical report, NEC Research Institute(1999)

    public class FAILED : Exception { }  // for merging
    public class found_a_solution : Exception { }

    public enum TLabel
    {
        Accept,
        Reject,
        Neutral
    }

    public enum Operation
    {
        Label,
        Child
    }

    public class Node
    {
        public int index;
        public TLabel label;
        public Dictionary<char, int> children;
        public Node(int index, TLabel label)
        {
            this.index = index;
            this.label = label;
            children = new();
        }
    }

    public static class MainProcedure
    {
        public static List<Node> apta = new();
        public static ImmutableList<int>? result;
        public static int max_red;
        public static bool finished_successfully;
        public static int comm_Rank;
        public static int comm_Size;
        public static int cutoff_count = 0; // Count of nodes at cutoff depth
        public static int? cutoff_depth; // Depth at which subtrees are divided among processes

        private static int insert_to_APTA(string word)
        {
            int current_node = 0;
            int i = 0;
            while (i < word.Length)
            {
                if (apta[current_node].children.ContainsKey(word[i]))
                {
                    current_node = apta[current_node].children[word[i]];
                }
                else
                {
                    int next_idx = apta.Count;
                    apta.Add(new Node(next_idx, TLabel.Neutral));
                    apta[current_node].children[word[i]] = next_idx;
                    current_node = next_idx;
                }
                ++i;
            }
            return current_node;
        }

        public static void build_APTA()
        {
            apta.Clear();
            Node root = new(index: 0, label: TLabel.Neutral);
            apta.Add(root);
            foreach (string word in InputData.Splus)
            {
                int end_node = insert_to_APTA(word);
                apta[end_node].label = TLabel.Accept;
            }
            foreach (string word in InputData.Sminus)
            {
                int end_node = insert_to_APTA(word);
                apta[end_node].label = TLabel.Reject;
            }
        }

        private static void walkit(int r, int b, Stack<(int, Operation, Object)> changes)
        {
            if (apta[b].label != TLabel.Neutral)
            {
                if (apta[r].label != TLabel.Neutral)
                {
                    if (apta[r].label != apta[b].label)
                        throw new FAILED();
                }
                else
                {
                    changes.Push((r, Operation.Label, TLabel.Neutral));
                    apta[r].label = apta[b].label;
                }
            }
            foreach (char i in InputData.alphabet)
            {
                int r_child;
                int b_child;
                if (apta[b].children.TryGetValue(i, out b_child))
                {
                    if (apta[r].children.TryGetValue(i, out r_child))
                    {
                        walkit(r_child, b_child, changes);
                    }
                    else
                    {
                        changes.Push((r, Operation.Child, new KeyValuePair<char, int?>(i, null)));
                        apta[r].children[i] = b_child;
                    }
                }
            }
        }

        private static void undo_merge(Stack<(int, Operation, Object)> changes)
        {
            while (changes.Any())
            {
                (int idx, Operation action, Object ob) = changes.Pop();
                if (action == Operation.Child)
                {
                    (char a, int? state) = (KeyValuePair<char, int?>)ob;
                    if (state is null)
                    {
                        apta[idx].children.Remove(a);
                    }
                    else
                    {
                        apta[idx].children[a] = (int)state;
                    }
                }
                else if (action == Operation.Label)
                {
                    apta[idx].label = (TLabel)ob;
                }
                else
                {
                    Console.WriteLine("Unknown operation to undo!");
                    Exit(1);
                }
            }
        }

        private static bool try_merge(int red_node, int blue_node, Stack<(int, Operation, Object)> changes)
        {
            for (int i = 0; i < apta.Count; ++i)
            {
                foreach ((char a, int state) in apta[i].children)
                {
                    if (state == blue_node)
                    {
                        changes.Push((i, Operation.Child, new KeyValuePair<char, int?>(a, state)));
                        apta[i].children[a] = red_node;
                    }
                }
            }
            try
            {
                walkit(red_node, blue_node, changes);
            }
            catch (FAILED)
            {
                return false;
            }
            return true;
        }

        private static int pick_blue_node(IList<int> blue_nodes, IList<int> red_nodes, 
            out UInt64 minval)
        {
            Dictionary<int, int> freq = new();
            foreach (int b in blue_nodes)
            {
                freq[b] = 0;
                foreach (int r in red_nodes)
                {
                    Stack<(int, Operation, Object)> changes = new();
                    if (try_merge(r, b, changes))
                    {
                        freq[b] += 1;
                    }
                    undo_merge(changes);
                }
            }
            minval = (UInt64)freq.MinBy(x => x.Value).Value;
            return freq.MinBy(x => x.Value).Key;
        }

        private static void exh_search(ImmutableList<int> red_list, int level, UInt64 product)
        {
            if (cutoff_depth is null)
            {
                if (product > 4 * (UInt64)comm_Size)
                {
                    cutoff_depth = level;
                    product = 0;
                }
            }
            else
            {
                if (level == cutoff_depth)
                {
                    if (++cutoff_count % comm_Size != comm_Rank)
                        return;
                }
            }
            if (red_list.Count <= max_red)
            {
                var red_set = red_list.ToImmutableHashSet();
                HashSet<int> blue_set = new();
                foreach (int r in red_list)
                {
                    foreach (int state in apta[r].children.Values)
                    {
                        if (!red_set.Contains(state))
                        {
                            blue_set.Add(state);
                        }
                    }
                }
                ImmutableList<int> blue_nodes = blue_set.ToImmutableList();
                if (blue_nodes.IsEmpty)
                {
                    result = red_list;
                    throw new found_a_solution();
                }
                else
                {
                    UInt64 minval;
                    int B = pick_blue_node(blue_nodes, red_list, out minval);
                    foreach (int R in red_list)
                    {
                        Stack<(int, Operation, Object)> changes = new();
                        if (try_merge(R, B, changes))
                        {
                            exh_search(red_list, level + 1, product * (minval + 1));
                        }
                        undo_merge(changes);
                    }
                    exh_search(red_list.Add(B), level + 1, product * (minval + 1));
                }
            }
        }

        public static void exbar_main(MPI.Intracommunicator comm)
        {
            finished_successfully = false;
            comm_Rank = comm.Rank;
            comm_Size = comm.Size;
            max_red = 1;
            build_APTA();
            while (true)
            {
                try
                {
                    exh_search(ImmutableList.Create(0), 0, 1);
                    ++max_red;
                    comm.Barrier();
                }
                catch (found_a_solution)
                {
                    finished_successfully = true;
                    break;
                }
            }
        }
    }
}
