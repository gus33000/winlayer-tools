using ICSharpCode.SharpZipLib.Tar;
using System.Collections.Generic;
using System.Text;

namespace LayerConverter
{
    public class ExtTarEntry : TarEntry
    {
        public IDictionary<string, string> Headers { get; internal set; }

        public ExtTarEntry(byte[] headerBuffer) : base(headerBuffer, Encoding.ASCII)
        {
        }
    }
}
