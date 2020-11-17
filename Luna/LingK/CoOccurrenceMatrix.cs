using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LingK
{
    public interface ICoOccurrenceRow<TRowKey, TColumnKey>
    {
        TRowKey RowKey { get; }
        bool ContainsKey(TColumnKey columnKey);
        int GetValue(TColumnKey columnKey, int defaultValue);
        bool TryGetValue(TColumnKey columnKey, out int value);
        void SetValue(TColumnKey columnKey, int value);
        int this[TColumnKey rowKey] { get; set; }
        IEnumerable<TColumnKey> GetColumnKeys();
    }

    public interface ICoOccurrenceColumn<TRowKey, TColumnKey> : IEnumerable<KeyValuePair<TRowKey, int>>
    {
        TColumnKey ColumnKey { get; }
        bool ContainsKey(TRowKey columnKey);
        int GetValue(TRowKey rowKey, int defaultValue);
        bool TryGetValue(TRowKey rowKey, out int value);
        void SetValue(TRowKey rowKey, int value);
        int Sum();
        int SumIf(Predicate<(TRowKey rowKey, int value)> predicate);
        int Count { get; }
        int this[TRowKey rowKey] { get; set; }
        IEnumerable<TRowKey> GetRowKeys();
    }

    public interface ICoOccurrenceMatrix<TRowKey, TColumnKey>
    {
        ICoOccurrenceColumn<TRowKey, TColumnKey> GetColumn(TColumnKey columnKey);
        ICoOccurrenceRow<TRowKey, TColumnKey> GetRow(TRowKey rowKey);
        int GetValue(TRowKey rowKey, TColumnKey columnKey, int defaultValue);
        bool TryGetValue(TRowKey rowKey, TColumnKey columnKey, out int value);
        void SetValue(TRowKey rowKey, TColumnKey columnKey, int value);

        IEnumerable<ICoOccurrenceColumn<TRowKey, TColumnKey>> GetColumns();
        IEnumerable<ICoOccurrenceRow<TRowKey, TColumnKey>> GetRows();

        IEnumerable<TColumnKey> GetColumnKeys();
        IEnumerable<TRowKey> GetRowKeys();

        int this[TRowKey rowKey, TColumnKey columnKey] { get; set; }
    }

    public class CoOccurrenceDict<TRowKey> : ICoOccurrenceColumn<TRowKey, string>
    {
        Dictionary<TRowKey, int> dict = new Dictionary<TRowKey, int>();

        public string ColumnKey => "";
        public bool ContainsKey(TRowKey rowKey) => dict.ContainsKey(rowKey);
        public int GetValue(TRowKey rowKey, int defaultValue) => dict.TryGetValue(rowKey, out int value) ? value : defaultValue;
        public bool TryGetValue(TRowKey rowKey, out int value) => dict.TryGetValue(rowKey, out value);
        public void SetValue(TRowKey rowKey, int value) => dict[rowKey] = value;
        public int Sum()
        {
            int sum = 0;
            foreach (var kvp in dict)
            {
                sum += kvp.Value;
            }
            return sum;
        }

        public int SumIf(Predicate<(TRowKey rowKey, int value)> predicate)
        {
            int sum = 0;
            foreach (var kvp in dict)
            {
                if (predicate((kvp.Key, kvp.Value)))
                {
                    sum += kvp.Value;
                }
            }
            return sum;
        }

        public IEnumerator<KeyValuePair<TRowKey, int>> GetEnumerator() => dict.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => dict.GetEnumerator();

        public IEnumerable<string> GetColumnKeys() => ColumnKey.Yield();
        public IEnumerable<TRowKey> GetRowKeys() => dict.Keys;

        public int Count { get => dict.Count; }
        public int this[TRowKey rowKey] { get => dict[rowKey]; set => dict[rowKey] = value; }
    }

    //TODO: load/save as .csv because it's smaller than json
    public class CoOccurrenceMatrix<TRowKey, TColumnKey> : ICoOccurrenceMatrix<TRowKey, TColumnKey>
    {
        private class Column : ICoOccurrenceColumn<TRowKey, TColumnKey>
        {
            private readonly CoOccurrenceMatrix<TRowKey, TColumnKey> myTable;
            private readonly TColumnKey thisKey;

            private bool isDirty;

            public TColumnKey ColumnKey => thisKey;

            public Column(CoOccurrenceMatrix<TRowKey, TColumnKey> owningTable, TColumnKey thisKey)
            {
                myTable = owningTable;
                this.thisKey = thisKey;
                isDirty = true;
            }

            public bool SetDirty() => isDirty = true;

            public bool ContainsKey(TRowKey rowKey)
            {
                var row = myTable.GetRow(rowKey);
                if (row == null) return false;

                return row.ContainsKey(thisKey);
            }

            public int GetValue(TRowKey rowKey, int defaultValue = default)
            {
                var row = myTable.GetRow(rowKey);
                if (row == null) return defaultValue;

                return row.GetValue(thisKey, defaultValue);
            }
            public bool TryGetValue(TRowKey rowKey, out int value)
            {
                value = 0;
                var row = myTable.GetRow(rowKey);
                if (row == null) return false;

                return row.TryGetValue(thisKey, out value);
            }

            public void SetValue(TRowKey rowKey, int value) => myTable.SetValue(rowKey, thisKey, value);
            public int this[TRowKey rowKey]
            {
                get => GetValue(rowKey);
                set => SetValue(rowKey, value);
            }

            private int _sum = 0;
            public int Sum()
            {
                CheckDirty();
                return _sum;
            }

            private int _rowCount = 0;
            public int Count
            {
                get
                {
                    CheckDirty();
                    return _rowCount;
                }
            }

            private void CheckDirty()
            {
                if (!isDirty) return;

                _sum = 0;
                _rowCount = 0;
                foreach (var kvp in myTable.rowDict)
                {
                    if (kvp.Value.TryGetValue(thisKey, out int value))
                    {
                        _rowCount++;
                        _sum += value;
                    }
                }
                isDirty = false;
            }

            public int SumIf(Predicate<(TRowKey rowKey, int value)> predicate)
            {
                int sum = 0;
                foreach (var kvp in myTable.rowDict)
                {
                    if (kvp.Value.TryGetValue(thisKey, out int value) && predicate((kvp.Key, value)))
                    {
                        sum += value;
                    }
                }
                return sum;
            }

            public IEnumerator<KeyValuePair<TRowKey, int>> GetEnumerator()
            {
                foreach (TRowKey rowKey in myTable.rowDict.Keys)
                {
                    if (TryGetValue(rowKey, out int value))
                    {
                        yield return new KeyValuePair<TRowKey, int>(rowKey, value);
                    }
                }
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<TRowKey> GetRowKeys()
            {
                foreach (TRowKey rowKey in myTable.rowDict.Keys)
                {
                    if (TryGetValue(rowKey, out int value))
                    {
                        yield return rowKey;
                    }
                }
            }
        }

        private class Row : ICoOccurrenceRow<TRowKey, TColumnKey>
        {
            [JsonIgnore]
            private readonly CoOccurrenceMatrix<TRowKey, TColumnKey> myTable;
            [JsonProperty("thisKey")]
            private readonly TRowKey thisKey;
            [JsonProperty("valuesDict")]
            private readonly Dictionary<TColumnKey, int> valuesDict = new Dictionary<TColumnKey, int>();
            [JsonIgnore]
            public TRowKey RowKey => thisKey;

            public Row(CoOccurrenceMatrix<TRowKey, TColumnKey> owningTable, TRowKey thisKey)
            {
                myTable = owningTable;
                this.thisKey = thisKey;
            }

            public bool ContainsKey(TColumnKey columnKey) => valuesDict.ContainsKey(columnKey);

            public int GetValue(TColumnKey columnKey, int defaultValue = default(int))
            {
                if (valuesDict.TryGetValue(columnKey, out int value))
                    return value;
                return defaultValue;
            }
            public bool TryGetValue(TColumnKey columnKey, out int value) => valuesDict.TryGetValue(columnKey, out value);

            public void SetValue(TColumnKey columnKey, int value)
            {
                myTable.columnKeys.Add(columnKey);
                valuesDict[columnKey] = value;
                if (myTable.columnCacheDict.TryGetValue(columnKey, out var column))
                {
                    column.SetDirty();
                }
            }
            public int this[TColumnKey columnKey]
            {
                get => GetValue(columnKey);
                set => SetValue(columnKey, value);
            }

            public IEnumerable<TColumnKey> GetColumnKeys() => valuesDict.Keys;
        }

        public delegate string SerializeRow(TRowKey rowKey);
        public delegate string SerializeColumn(TColumnKey columnKey);
        public delegate TRowKey DeSerializeRow(string rowString);
        public delegate TColumnKey DeSerializeColumn(string columnString);

        static SerializeRow defaultSerializeRow = x => JsonConvert.SerializeObject(x);
        static SerializeColumn defaultSerializeColumn = x => JsonConvert.SerializeObject(x);
        static DeSerializeRow defaultDeSerializeRow = x => JsonConvert.DeserializeObject<TRowKey>(x);
        static DeSerializeColumn defaultDeSerializeColumn = x => JsonConvert.DeserializeObject<TColumnKey>(x);

        SerializeRow serializeRow;
        SerializeColumn serializeColumn;
        DeSerializeRow deserializeRow;
        DeSerializeColumn deserializeColumn;

        [JsonProperty("rowDict")]
        private readonly Dictionary<TRowKey, Row> rowDict = new Dictionary<TRowKey, Row>();
        [JsonProperty("columnKeys")]
        private readonly HashSet<TColumnKey> columnKeys = new HashSet<TColumnKey>();
        [JsonIgnore]
        private readonly Dictionary<TColumnKey, Column> columnCacheDict = new Dictionary<TColumnKey, Column>();

        public CoOccurrenceMatrix(SerializeRow serializeRow = null, DeSerializeRow deserializeRow = null, SerializeColumn serializeColumn = null, DeSerializeColumn deserializeColumn = null)
        {

            this.serializeRow = serializeRow ?? defaultSerializeRow;
            this.serializeColumn = serializeColumn ?? defaultSerializeColumn;
            this.deserializeRow = deserializeRow ?? defaultDeSerializeRow;
            this.deserializeColumn = deserializeColumn ?? defaultDeSerializeColumn;
        }

        public void Clear()
        {
            rowDict.Clear();
            columnKeys.Clear();
            columnCacheDict.Clear();
        }

        public ICoOccurrenceColumn<TRowKey, TColumnKey> GetColumn(TColumnKey columnKey)
        {
            if (!columnCacheDict.TryGetValue(columnKey, out var column))
            {
                columnCacheDict[columnKey] = column = new Column(this, columnKey);
            }
            return column;
        }

        public ICoOccurrenceRow<TRowKey, TColumnKey> GetRow(TRowKey rowKey)
        {
            if (rowDict.TryGetValue(rowKey, out Row value))
            {
                return value;
            }
            return null;
        }

        public void SetValue(TRowKey rowKey, TColumnKey columnKey, int value)
        {
            if (!rowDict.TryGetValue(rowKey, out Row row))
            {
                rowDict[rowKey] = row = new Row(this, rowKey);
            }

            row.SetValue(columnKey, value);
        }

        public int GetValue(TRowKey rowKey, TColumnKey columnKey, int defaultValue = default)
        {
            if (rowDict.TryGetValue(rowKey, out Row row))
            {
                return row.GetValue(columnKey, defaultValue);
            }

            return defaultValue;
        }
        public bool TryGetValue(TRowKey rowKey, TColumnKey columnKey, out int value)
        {
            value = 0;
            if (rowDict.TryGetValue(rowKey, out Row row))
            {
                return row.TryGetValue(columnKey, out value);
            }

            return false;
        }

        public IEnumerable<ICoOccurrenceColumn<TRowKey, TColumnKey>> GetColumns()
        {
            foreach (TColumnKey columnKey in columnKeys)
            {
                yield return GetColumn(columnKey);
            }
        }
        public IEnumerable<ICoOccurrenceRow<TRowKey, TColumnKey>> GetRows()
        {
            return rowDict.Values;
        }

        public IEnumerable<TColumnKey> GetColumnKeys()
        {
            return columnKeys;
        }
        public IEnumerable<TRowKey> GetRowKeys()
        {
            return rowDict.Keys;
        }

        public int this[TRowKey rowKey, TColumnKey columnKey]
        {
            get => GetValue(rowKey, columnKey);
            set => SetValue(rowKey, columnKey, value);
        }

        public void Load(string file)
        {
            using (StreamReader reader = new StreamReader(file))
            {
                CSVReader csvReader = new CSVReader(reader, '\t');

                List<TColumnKey> header = new List<TColumnKey>();
                csvReader.ReadRow().ForEach(x => header.Add(deserializeColumn(x)));

                List<string> row;
                while ((row = csvReader.ReadRow()).Count > 0)
                {
                    TRowKey rowKey = deserializeRow(row[0]);
                    for (int i = 1; i < header.Count && i < row.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(row[i]) && int.TryParse(row[i], out int value))
                        {
                            SetValue(rowKey, header[i], value);
                        }
                    }
                }
            }
        }

        public void Save(string file)
        {
            using (StreamWriter writer = new StreamWriter(file))
            {
                CSVWriter csvWriter = new CSVWriter(writer, '\t');
                TColumnKey[] columns = columnKeys.ToArray();

                // header
                csvWriter.AddCell("keys");
                for (int i = 0; i < columns.Length; i++)
                {
                    csvWriter.AddCell(serializeColumn(columns[i]));
                }
                csvWriter.NewLine();

                //data
                foreach (var kvp in rowDict)
                {
                    csvWriter.AddCell(serializeRow(kvp.Key));
                    for (int i = 0; i < columns.Length; i++)
                    {
                        csvWriter.AddCell(kvp.Value[columns[i]].ToString());
                    }
                    csvWriter.NewLine();
                }
            }
        }
    }
}
