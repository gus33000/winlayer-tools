using ICSharpCode.SharpZipLib.Tar;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LayerConverter
{
    class ExtTarInputStream : TarInputStream
    {
        private ExtTarEntry currentEntry;
        private Dictionary<string, string> curHeaders;
        private StringBuilder longLink;

        public ExtTarInputStream(Stream inputStream) : base(inputStream, TarBuffer.DefaultBlockFactor, Encoding.ASCII)
        {

        }

        private void SkipToNextEntry()
        {
            long numToSkip = entrySize - entryOffset;

            if (numToSkip > 0)
            {
                Skip(numToSkip);
            }

            readBuffer = null;
        }

        /// <summary>
        /// Get the next entry in this tar archive. This will skip
        /// over any remaining data in the current entry, if there
        /// is one, and place the input stream at the header of the
        /// next entry, and read the header and instantiate a new
        /// TarEntry from the header bytes and return that entry.
        /// If there are no more entries in the archive, null will
        /// be returned to indicate that the end of the archive has
        /// been reached.
        /// </summary>
        /// <returns>
        /// The next TarEntry in the archive, or null.
        /// </returns>
        public ExtTarEntry GetNextExtEntry()
        {
            if (hasHitEOF)
            {
                return null;
            }

            if (currentEntry != null)
            {
                SkipToNextEntry();
            }

            byte[] headerBuf = tarBuffer.ReadBlock();

            if (headerBuf == null)
            {
                hasHitEOF = true;
            }
            else if (TarBuffer.IsEndOfArchiveBlock(headerBuf))
            {
                hasHitEOF = true;

                // Read the second zero-filled block
                tarBuffer.ReadBlock();
            }
            else
            {
                hasHitEOF = false;
            }

            if (hasHitEOF)
            {
                currentEntry = null;
            }
            else
            {
                try
                {
                    var header = new TarHeader();
                    header.ParseBuffer(headerBuf, Encoding.ASCII);
                    if (!header.IsChecksumValid)
                    {
                        throw new TarException("Header checksum is invalid");
                    }
                    this.entryOffset = 0;
                    this.entrySize = header.Size;

                    StringBuilder longName = null;

                    if (header.TypeFlag == TarHeader.LF_GNU_LONGNAME)
                    {
                        byte[] nameBuffer = new byte[TarBuffer.BlockSize];
                        long numToRead = this.entrySize;

                        longName = new StringBuilder();

                        while (numToRead > 0)
                        {
                            int numRead = this.Read(nameBuffer, 0, (numToRead > nameBuffer.Length ? nameBuffer.Length : (int)numToRead));

                            if (numRead == -1)
                            {
                                throw new InvalidHeaderException("Failed to read long name entry");
                            }

                            longName.Append(TarHeader.ParseName(nameBuffer, 0, numRead, Encoding.ASCII).ToString());
                            numToRead -= numRead;
                        }

                        SkipToNextEntry();
                        headerBuf = this.tarBuffer.ReadBlock();
                    }
                    else if (header.TypeFlag == TarHeader.LF_GHDR)
                    {  // POSIX global extended header
                       // Ignore things we dont understand completely for now
                        SkipToNextEntry();
                        headerBuf = this.tarBuffer.ReadBlock();
                    }
                    else if (header.TypeFlag == TarHeader.LF_XHDR)
                    {  // POSIX extended header
                        byte[] nameBuffer = new byte[TarBuffer.BlockSize];
                        long numToRead = this.entrySize;

                        var xhr = new TarExtendedHeaderReader();

                        while (numToRead > 0)
                        {
                            int numRead = this.Read(nameBuffer, 0, (numToRead > nameBuffer.Length ? nameBuffer.Length : (int)numToRead));

                            if (numRead == -1)
                            {
                                throw new InvalidHeaderException("Failed to read long name entry");
                            }

                            xhr.Read(nameBuffer, numRead);
                            numToRead -= numRead;
                        }

                        curHeaders = xhr.Headers;

                        if (xhr.Headers.TryGetValue("path", out string name))
                        {
                            longName = new StringBuilder(name);
                        }

                        if (xhr.Headers.TryGetValue("linkpath", out name))
                        {
                            longLink = new StringBuilder(name);
                        }

                        SkipToNextEntry();
                        headerBuf = this.tarBuffer.ReadBlock();
                    }
                    else if (header.TypeFlag == TarHeader.LF_GNU_VOLHDR)
                    {
                        // TODO: could show volume name when verbose
                        SkipToNextEntry();
                        headerBuf = this.tarBuffer.ReadBlock();
                    }
                    else if (header.TypeFlag != TarHeader.LF_NORMAL &&
                             header.TypeFlag != TarHeader.LF_OLDNORM &&
                             header.TypeFlag != TarHeader.LF_LINK &&
                             header.TypeFlag != TarHeader.LF_SYMLINK &&
                             header.TypeFlag != TarHeader.LF_DIR)
                    {
                        // Ignore things we dont understand completely for now
                        SkipToNextEntry();
                        headerBuf = tarBuffer.ReadBlock();
                    }

                    currentEntry = new ExtTarEntry(headerBuf);
                    if (longName != null)
                    {
                        currentEntry.Name = longName.ToString();
                        currentEntry.TarHeader.Name = longName.ToString();
                    }
                    if (longLink != null)
                    {
                        currentEntry.TarHeader.LinkName = longLink.ToString();
                        longLink = null;
                    }
                    if (curHeaders != null)
                    {
                        currentEntry.Headers = curHeaders;
                        curHeaders = null;
                    }

                    // Magic was checked here for 'ustar' but there are multiple valid possibilities
                    // so this is not done anymore.

                    entryOffset = 0;

                    // TODO: Review How do we resolve this discrepancy?!
                    entrySize = this.currentEntry.Size;
                }
                catch (InvalidHeaderException ex)
                {
                    entrySize = 0;
                    entryOffset = 0;
                    currentEntry = null;
                    string errorText = string.Format("Bad header in record {0} block {1} {2}",
                        tarBuffer.CurrentRecord, tarBuffer.CurrentBlock, ex.Message);
                    throw new InvalidHeaderException(errorText);
                }
            }
            return currentEntry;
        }

    }
}
