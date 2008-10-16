/* Copyright (c) 2006-2007, Peter Golde
 * All rights reserved.
 * 
 * Redistribution and use in source and binary forms, with or without 
 * modification, are permitted provided that the following conditions are 
 * met:
 * 
 * 1. Redistributions of source code must retain the above copyright
 * notice, this list of conditions and the following disclaimer.
 * 
 * 2. Redistributions in binary form must reproduce the above copyright
 * notice, this list of conditions and the following disclaimer in the
 * documentation and/or other materials provided with the distribution.
 * 
 * 3. Neither the name of Peter Golde, nor "Purple Pen", nor the names
 * of its contributors may be used to endorse or promote products
 * derived from this software without specific prior written permission.
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES,
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY,
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE
 * USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY
 * OF SUCH DAMAGE.
 */

using System;
using System.IO;

namespace PurplePen.MapModel
{
    // Class that manages input and output automatically. All static.
    public class InputOutput
    {
        private InputOutput()
        {
        }

        // Read a file into the given map. Returns the file format
        // of the file.
        public static int ReadFile(string filename, Map map)
        {
            // Determine the file type, and open it up.
            using (Stream stm = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (OcadImport.IsOcadFile(stm)) {
                    OcadImport importer = new OcadImport(map);
                    int format = importer.ReadOcadFile(stm, filename);
                    return format;
                }
                else {
                    // CONSIDER: do something more useful here
                    throw new ApplicationException("File is not an OCAD file");
                }
            }
        }

        public static void WriteFile(string filename, Map map, int format)
        {
            OcadExport o = new OcadExport();
            o.WriteMap(map, filename, format, true);
        }
    }
}
