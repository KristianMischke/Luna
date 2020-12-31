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
    public interface ICoOccurrenceRow<TRowKey, TColumnKey, TValue>
    {
        TRowKey RowKey { get; }
        bool ContainsKey(TColumnKey columnKey);
        TValue GetValue(TColumnKey columnKey, TValue defaultValue);
        bool TryGetValue(TColumnKey columnKey, out TValue value);
        void SetValue(TColumnKey columnKey, TValue value);
        TValue this[TColumnKey columnKey] { get; set; }
        IEnumerable<TColumnKey> GetColumnKeys();
    }

    public interface ICoOccurrenceColumn<TRowKey, TColumnKey, TValue> : IEnumerable<KeyValuePair<TRowKey, TValue>>
    {
        TColumnKey ColumnKey { get; }
        bool ContainsKey(TRowKey columnKey);
        TValue GetValue(TRowKey rowKey, TValue defaultValue);
        bool TryGetValue(TRowKey rowKey, out TValue value);
        void SetValue(TRowKey rowKey, TValue value);
        TValue Sum();
        TValue SumIf(Predicate<(TRowKey rowKey, TValue value)> predicate);
        int Count { get; }
        TValue this[TRowKey rowKey] { get; set; }
        IEnumerable<TRowKey> GetRowKeys();
    }

    public interface ICoOccurrenceMatrix<TRowKey, TColumnKey, TValue>
    {
        ICoOccurrenceColumn<TRowKey, TColumnKey, TValue> GetColumn(TColumnKey columnKey);
        ICoOccurrenceRow<TRowKey, TColumnKey, TValue> GetRow(TRowKey rowKey);
        TValue GetValue(TRowKey rowKey, TColumnKey columnKey, TValue defaultValue);
        bool TryGetValue(TRowKey rowKey, TColumnKey columnKey, out TValue value);
        void SetValue(TRowKey rowKey, TColumnKey columnKey, TValue value);

        IEnumerable<ICoOccurrenceColumn<TRowKey, TColumnKey, TValue>> GetColumns();
        IEnumerable<ICoOccurrenceRow<TRowKey, TColumnKey, TValue>> GetRows();

        IEnumerable<TColumnKey> GetColumnKeys();
        IEnumerable<TRowKey> GetRowKeys();

        TValue this[TRowKey rowKey, TColumnKey columnKey] { get; set; }
    }

    public class CoOccurrenceDict<TRowKey, TValue> : ICoOccurrenceColumn<TRowKey, string, TValue>
    {
        Dictionary<TRowKey, TValue> dict = new Dictionary<TRowKey, TValue>();
        INumericPolicy<TValue> policy = (INumericPolicy<TValue>)NumericPolicies.Instance;

        public string ColumnKey => "";
        public bool ContainsKey(TRowKey rowKey) => dict.ContainsKey(rowKey);
        public TValue GetValue(TRowKey rowKey, TValue defaultValue) => dict.TryGetValue(rowKey, out TValue value) ? value : defaultValue;
        public bool TryGetValue(TRowKey rowKey, out TValue value) => dict.TryGetValue(rowKey, out value);
        public void SetValue(TRowKey rowKey, TValue value) => dict[rowKey] = value;
        public TValue Sum()
        {
            TValue sum = policy.Zero();
            foreach (var kvp in dict)
            {
                policy.Increment(ref sum, kvp.Value);
            }
            return sum;
        }

        public TValue SumIf(Predicate<(TRowKey rowKey, TValue value)> predicate)
        {
            TValue sum = policy.Zero();
            foreach (var kvp in dict)
            {
                if (predicate((kvp.Key, kvp.Value)))
                {
                    policy.Increment(ref sum, kvp.Value);
                }
            }
            return sum;
        }

        public IEnumerator<KeyValuePair<TRowKey, TValue>> GetEnumerator() => dict.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => dict.GetEnumerator();

        public IEnumerable<string> GetColumnKeys() => ColumnKey.Yield();
        public IEnumerable<TRowKey> GetRowKeys() => dict.Keys;

        public int Count { get => dict.Count; }
        public TValue this[TRowKey rowKey] { get => dict[rowKey]; set => dict[rowKey] = value; }
    }

    public class UnigramMatrix<TValue> : CoOccurrenceMatrix<string, string, TValue> where TValue : struct
    {
        public UnigramMatrix(SerializeRow serializeRow = null, DeSerializeRow deserializeRow = null, SerializeColumn serializeColumn = null, DeSerializeColumn deserializeColumn = null) : base(serializeRow, deserializeRow, serializeColumn, deserializeColumn) {}
    }
    public class BigramMatrix<TValue> : CoOccurrenceMatrix<(string, string), string, TValue> where TValue : struct
    {
        public BigramMatrix(SerializeRow serializeRow = null, DeSerializeRow deserializeRow = null, SerializeColumn serializeColumn = null, DeSerializeColumn deserializeColumn = null) : base(serializeRow, deserializeRow, serializeColumn, deserializeColumn) { }
    }
    public class TrigramMatrix<TValue> : CoOccurrenceMatrix<(string, string, string), string, TValue> where TValue : struct
    {
        public TrigramMatrix(SerializeRow serializeRow = null, DeSerializeRow deserializeRow = null, SerializeColumn serializeColumn = null, DeSerializeColumn deserializeColumn = null) : base(serializeRow, deserializeRow, serializeColumn, deserializeColumn) { }
    }

    public class CoOccurrenceMatrix<TRowKey, TColumnKey, TValue> : ICoOccurrenceMatrix<TRowKey, TColumnKey, TValue> where TValue : struct
    {
        static INumericPolicy<TValue> policy = (INumericPolicy<TValue>)NumericPolicies.Instance;

        private class Column : ICoOccurrenceColumn<TRowKey, TColumnKey, TValue>
        {
            private readonly CoOccurrenceMatrix<TRowKey, TColumnKey, TValue> myTable;
            private readonly TColumnKey thisKey;

            private bool isDirty;

            public TColumnKey ColumnKey => thisKey;

            public Column(CoOccurrenceMatrix<TRowKey, TColumnKey, TValue> owningTable, TColumnKey thisKey)
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

            public TValue GetValue(TRowKey rowKey, TValue defaultValue = default)
            {
                var row = myTable.GetRow(rowKey);
                if (row == null) return defaultValue;

                return row.GetValue(thisKey, defaultValue);
            }
            public bool TryGetValue(TRowKey rowKey, out TValue value)
            {
                value = policy.Zero();
                var row = myTable.GetRow(rowKey);
                if (row == null) return false;

                return row.TryGetValue(thisKey, out value);
            }

            public void SetValue(TRowKey rowKey, TValue value) => myTable.SetValue(rowKey, thisKey, value);
            public TValue this[TRowKey rowKey]
            {
                get => GetValue(rowKey);
                set => SetValue(rowKey, value);
            }

            private TValue _sum = policy.Zero();
            public TValue Sum()
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

                _sum = policy.Zero();
                _rowCount = 0;
                foreach (var kvp in myTable.rowDict)
                {
                    if (kvp.Value.TryGetValue(thisKey, out TValue value))
                    {
                        _rowCount++;
                        policy.Increment(ref _sum, value);
                    }
                }
                isDirty = false;
            }

            public TValue SumIf(Predicate<(TRowKey rowKey, TValue value)> predicate)
            {
                TValue sum = policy.Zero();
                foreach (var kvp in myTable.rowDict)
                {
                    if (kvp.Value.TryGetValue(thisKey, out TValue value) && predicate((kvp.Key, value)))
                    {
                        policy.Increment(ref _sum, value);
                    }
                }
                return sum;
            }

            public IEnumerator<KeyValuePair<TRowKey, TValue>> GetEnumerator()
            {
                foreach (TRowKey rowKey in myTable.rowDict.Keys)
                {
                    if (TryGetValue(rowKey, out TValue value))
                    {
                        yield return new KeyValuePair<TRowKey, TValue>(rowKey, value);
                    }
                }
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public IEnumerable<TRowKey> GetRowKeys()
            {
                foreach (TRowKey rowKey in myTable.rowDict.Keys)
                {
                    if (TryGetValue(rowKey, out _))
                    {
                        yield return rowKey;
                    }
                }
            }
        }

        private class Row : ICoOccurrenceRow<TRowKey, TColumnKey, TValue>
        {
            [JsonIgnore]
            private readonly CoOccurrenceMatrix<TRowKey, TColumnKey, TValue> myTable;
            [JsonProperty("thisKey")]
            private readonly TRowKey thisKey;
            [JsonProperty("valuesDict")]
            private readonly Dictionary<TColumnKey, TValue> valuesDict = new Dictionary<TColumnKey, TValue>();
            [JsonIgnore]
            public TRowKey RowKey => thisKey;

            public Row(CoOccurrenceMatrix<TRowKey, TColumnKey, TValue> owningTable, TRowKey thisKey)
            {
                myTable = owningTable;
                this.thisKey = thisKey;
            }

            public bool ContainsKey(TColumnKey columnKey) => valuesDict.ContainsKey(columnKey);

            public TValue GetValue(TColumnKey columnKey, TValue defaultValue = default(TValue))
            {
                if (valuesDict.TryGetValue(columnKey, out TValue value))
                    return value;
                return defaultValue;
            }
            public bool TryGetValue(TColumnKey columnKey, out TValue value) => valuesDict.TryGetValue(columnKey, out value);

            public void SetValue(TColumnKey columnKey, TValue value)
            {
                myTable.columnKeys.Add(columnKey);
                valuesDict[columnKey] = value;
                if (myTable.columnCacheDict.TryGetValue(columnKey, out var column))
                {
                    column.SetDirty();
                }
            }
            public TValue this[TColumnKey columnKey]
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

        public ICoOccurrenceColumn<TRowKey, TColumnKey, TValue> GetColumn(TColumnKey columnKey)
        {
            if (!columnCacheDict.TryGetValue(columnKey, out var column))
            {
                columnCacheDict[columnKey] = column = new Column(this, columnKey);
            }
            return column;
        }

        public ICoOccurrenceRow<TRowKey, TColumnKey, TValue> GetRow(TRowKey rowKey)
        {
            if (rowDict.TryGetValue(rowKey, out Row value))
            {
                return value;
            }
            return null;
        }

        public void SetValue(TRowKey rowKey, TColumnKey columnKey, TValue value)
        {
            if (!rowDict.TryGetValue(rowKey, out Row row))
            {
                rowDict[rowKey] = row = new Row(this, rowKey);
            }

            row.SetValue(columnKey, value);
        }
        
        public TValue GetValue(TRowKey rowKey, TColumnKey columnKey, TValue defaultValue = default)
        {
            if (rowDict.TryGetValue(rowKey, out Row row))
            {
                return row.GetValue(columnKey, defaultValue);
            }

            return defaultValue;
        }
        public bool TryGetValue(TRowKey rowKey, TColumnKey columnKey, out TValue value)
        {
            value = policy.Zero();
            if (rowDict.TryGetValue(rowKey, out Row row))
            {
                return row.TryGetValue(columnKey, out value);
            }

            return false;
        }

        public void IncrementValue(TRowKey rowKey, TColumnKey columnKey, TValue amount)
        {
            if (!rowDict.TryGetValue(rowKey, out Row row))
            {
                rowDict[rowKey] = row = new Row(this, rowKey);
            }

            row.SetValue(columnKey, policy.Add(row.GetValue(columnKey), amount));
        }

        public IEnumerable<ICoOccurrenceColumn<TRowKey, TColumnKey, TValue>> GetColumns()
        {
            foreach (TColumnKey columnKey in columnKeys)
            {
                yield return GetColumn(columnKey);
            }
        }
        public IEnumerable<ICoOccurrenceRow<TRowKey, TColumnKey, TValue>> GetRows()
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

        public TValue this[TRowKey rowKey, TColumnKey columnKey]
        {
            get => GetValue(rowKey, columnKey);
            set => SetValue(rowKey, columnKey, value);
        }

        public void Load(string file, char delimeter = '\t', bool increment = false, List<TColumnKey> overrideHeader = null, Func<TColumnKey, TColumnKey> modifyHeader = null, TColumnKey rowKeyColumn = default, TValue? defaultValue = null)
        {
            if (modifyHeader == null)
            {
                modifyHeader = (x) => x;
            }
            using (StreamReader reader = new StreamReader(file))
            {
                CSVReader csvReader = new CSVReader(reader, delimeter);

                List<TColumnKey> header;
                if (overrideHeader != null)
                {
                    header = overrideHeader;
                }
                else
                {
                    header = new List<TColumnKey>();
                    csvReader.ReadRow().ForEach(x => header.Add(modifyHeader(deserializeColumn(x))));
                }

                int rowKeyColumnIndex = header.IndexOf(rowKeyColumn);
                if (rowKeyColumnIndex == -1 || rowKeyColumn.Equals(default(TColumnKey)))
                    rowKeyColumnIndex = 0;

                List<string> row;
                while ((row = csvReader.ReadRow()).Count > 0)
                {
                    TRowKey rowKey = deserializeRow(row[rowKeyColumnIndex]);
                    for (int i = 0; i < header.Count; i++)
                    {
                        if (i != rowKeyColumnIndex)
                        {
                            if (i < row.Count)
                            {
                                if (!string.IsNullOrEmpty(row[i]) && policy.TryParse(row[i], out TValue value))
                                {
                                    if (increment)
                                    {
                                        IncrementValue(rowKey, header[i], value);
                                    }
                                    else
                                    {
                                        SetValue(rowKey, header[i], value);
                                    }
                                }
                            }
                            else if (defaultValue.HasValue)
                            {
                                if (increment)
                                {
                                    IncrementValue(rowKey, header[i], defaultValue.Value);
                                }
                                else
                                {
                                    SetValue(rowKey, header[i], defaultValue.Value);
                                }
                            }
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
