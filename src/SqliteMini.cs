using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MouseModes
{
    /// <summary>
    /// Leitor mínimo, só leitura, de tabelas SQLite (sem dependências).
    /// Usado para ler os estágios de DPI que o programa da Darmoshark guarda no banco dele.
    /// Suporta tabelas comuns (rowid) sem overflow; linhas que não couberem numa página são ignoradas.
    /// </summary>
    public sealed class SqliteMini
    {
        private readonly byte[] db;
        private readonly int pageSize;
        private readonly Encoding text;

        public SqliteMini(string path)
        {
            // Abre compartilhado: o programa da Darmoshark pode estar com o arquivo aberto.
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var ms = new MemoryStream())
            {
                fs.CopyTo(ms);
                db = ms.ToArray();
            }
            if (db.Length < 100 || Encoding.ASCII.GetString(db, 0, 15) != "SQLite format 3")
                throw new InvalidDataException("não é um banco SQLite comum (pode estar criptografado)");
            int ps = (db[16] << 8) | db[17];
            pageSize = ps == 1 ? 65536 : ps;
            int enc = ReadInt32BE(db, 56);
            text = enc == 2 ? Encoding.Unicode : enc == 3 ? Encoding.BigEndianUnicode : Encoding.UTF8;
        }

        /// <summary>
        /// Todas as linhas da tabela, cada uma como lista de valores (long, double, string, byte[] ou null).
        /// Se a primeira coluna for a chave (INTEGER PRIMARY KEY), ela vem preenchida com o rowid.
        /// </summary>
        public List<object[]> ReadTable(string table)
        {
            int root = -1;
            foreach (object[] row in ReadBTree(1))
            {
                // sqlite_master: type, name, tbl_name, rootpage, sql
                if (row.Length >= 4 && "table".Equals(row[0]) && table.Equals(row[1] as string, StringComparison.OrdinalIgnoreCase))
                {
                    root = Convert.ToInt32(row[3]);
                    break;
                }
            }
            if (root < 1) throw new InvalidDataException("tabela " + table + " não encontrada");
            return ReadBTree(root);
        }

        private List<object[]> ReadBTree(int page)
        {
            var rows = new List<object[]>();
            var stack = new Stack<int>();
            var seen = new HashSet<int>();
            stack.Push(page);
            while (stack.Count > 0)
            {
                int p = stack.Pop();
                if (p < 1 || !seen.Add(p) || (long)p * pageSize > db.Length) continue;
                int start = (p - 1) * pageSize;
                int hdr = p == 1 ? start + 100 : start;
                byte type = db[hdr];
                int cells = (db[hdr + 3] << 8) | db[hdr + 4];
                bool interior = type == 0x05;
                if (type != 0x05 && type != 0x0D) continue; // só tabelas
                int ptrArray = hdr + (interior ? 12 : 8);
                if (interior) stack.Push(ReadInt32BE(db, hdr + 8)); // filho mais à direita
                for (int i = 0; i < cells; i++)
                {
                    int cell = start + ((db[ptrArray + i * 2] << 8) | db[ptrArray + i * 2 + 1]);
                    if (interior)
                    {
                        stack.Push(ReadInt32BE(db, cell));
                        continue;
                    }
                    int pos = cell;
                    long payload = ReadVarint(ref pos);
                    long rowid = ReadVarint(ref pos);
                    int maxLocal = pageSize - 35;
                    if (payload > maxLocal) continue; // linha com overflow: não precisamos dessas
                    object[] values = ReadRecord(pos);
                    // "INTEGER PRIMARY KEY" é guardado como rowid e aparece nulo no registro.
                    if (values.Length > 0 && values[0] == null) values[0] = rowid;
                    rows.Add(values);
                }
            }
            return rows;
        }

        private object[] ReadRecord(int pos)
        {
            int recStart = pos;
            long headerSize = ReadVarint(ref pos);
            int headerEnd = recStart + (int)headerSize;
            var types = new List<long>();
            while (pos < headerEnd) types.Add(ReadVarint(ref pos));
            int data = headerEnd;
            var values = new object[types.Count];
            for (int i = 0; i < types.Count; i++)
            {
                long t = types[i];
                switch (t)
                {
                    case 0: values[i] = null; break;
                    case 1: values[i] = (long)(sbyte)db[data]; data += 1; break;
                    case 2: values[i] = (long)(short)((db[data] << 8) | db[data + 1]); data += 2; break;
                    case 3:
                        int v3 = (db[data] << 16) | (db[data + 1] << 8) | db[data + 2];
                        if ((v3 & 0x800000) != 0) v3 -= 0x1000000; // estende o sinal de 24 bits
                        values[i] = (long)v3;
                        data += 3;
                        break;
                    case 4: values[i] = (long)ReadInt32BE(db, data); data += 4; break;
                    case 5: values[i] = ReadIntBE(data, 6); data += 6; break;
                    case 6: values[i] = ReadIntBE(data, 8); data += 8; break;
                    case 7: values[i] = BitConverter.Int64BitsToDouble(ReadIntBE(data, 8)); data += 8; break;
                    case 8: values[i] = 0L; break;
                    case 9: values[i] = 1L; break;
                    default:
                        if (t >= 12)
                        {
                            int len = (int)((t - (t % 2 == 0 ? 12 : 13)) / 2);
                            if (t % 2 == 1) values[i] = text.GetString(db, data, len);
                            else { var b = new byte[len]; Buffer.BlockCopy(db, data, b, 0, len); values[i] = b; }
                            data += len;
                        }
                        break;
                }
            }
            return values;
        }

        private long ReadVarint(ref int pos)
        {
            long v = 0;
            for (int i = 0; i < 9; i++)
            {
                byte b = db[pos++];
                if (i == 8) return (v << 8) | (long)b;
                v = (v << 7) | (b & 0x7FL);
                if ((b & 0x80) == 0) break;
            }
            return v;
        }

        private long ReadIntBE(int pos, int bytes)
        {
            long v = (sbyte)db[pos];
            for (int i = 1; i < bytes; i++) v = (v << 8) | (long)db[pos + i];
            return v;
        }

        private static int ReadInt32BE(byte[] b, int pos)
        {
            return (b[pos] << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3];
        }
    }
}
