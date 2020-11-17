using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LingK
{
    class CSVReader
    {
        TextReader reader;
        char separator;

        public CSVReader(TextReader reader, char separator = ',')
        {
            this.reader = reader;
            this.separator = separator;
        }

        public List<string> ReadRow()
        {
            string line = reader.ReadLine();
            List<string> result = new List<string>();
            if (line == null) return result;

            bool isEscaped = false;
            StringBuilder nextItem = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    if (isEscaped && i + 1 < line.Length && line[i + 1] == '"') // double quote is escaped
                    {
                        nextItem.Append(line[i]);
                        i++;
                    }
                    else
                    {
                        isEscaped = !isEscaped;
                    }
                }
                else if (line[i] == '\\' && i + 1 < line.Length)
                {
                    if (line[i + 1] == 'n')
                    {
                        nextItem.Append('\n');
                        i++;
                    }
                    else if (line[i + 1] == 'r')
                    {
                        nextItem.Append('\r');
                        i++;
                    }
                    else if (line[i + 1] == '\\')
                    {
                        nextItem.Append('\\');
                        i++;
                    }
                }
                else if (line[i] == separator && !isEscaped)
                {
                    result.Add(nextItem.ToString());
                    nextItem.Clear();
                }
                else
                {
                    nextItem.Append(line[i]);
                }
            }

            if (nextItem.Length > 0) // add final item
            {
                result.Add(nextItem.ToString());
                nextItem.Clear();
            }

            return result;
        }

    }

    class CSVWriter
    {
        TextWriter writer;
        char separator;

        public CSVWriter(TextWriter writer, char separator = ',')
        {
            this.writer = writer;
            this.separator = separator;
        }

        public void AddCell(string text)
        {
            WriteEscapedString(text);
            writer.Write(separator);
        }

        public void NewLine() => writer.WriteLine();

        /// <summary>
        ///     <para>Turn a string into a CSV cell output.</para>
        ///     <para>Adaptation of https://stackoverflow.com/questions/6377454/escaping-tricky-string-to-csv-format by Ed Bayiates</para>
        /// </summary>
        private void WriteEscapedString(string str)
        {
            bool mustQuote = (str.Contains(separator.ToString()) || str.Contains("\"") || str.Contains("\r") || str.Contains("\n") || str.Contains("\\"));
            if (mustQuote)
            {
                writer.Write("\"");
                foreach (char nextChar in str)
                {
                    if (nextChar == '\\')
                    {
                        writer.Write("\\\\");
                    }
                    else if (nextChar == '\r')
                    {
                        writer.Write("\\r");
                    }
                    else if (nextChar == '\n')
                    {
                        writer.Write("\\n");
                    }
                    else
                    {
                        writer.Write(nextChar);
                        if (nextChar == '"')
                            writer.Write("\"");
                    }
                }
                writer.Write("\"");
                return;
            }

            writer.Write(str);
        }
    }
}
