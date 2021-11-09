using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExperimentProject
{
    public interface IData
    {
        int calc();
    }

    class Implementation : IData
    {
        public int calc()
        {
            return 2 * 2;
        }
    }

    public class TestedClass
    {
        public int foo(int d)
        {
            return data.calc() * d;
        }

        public TestedClass(IData data)
        {
            this.data = data;
        }
            
        private IData data;
    }


    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
