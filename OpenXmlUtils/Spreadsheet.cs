﻿#region File Information
//
// File: "Spreadsheet.cs"
// Purpose: "Create xlxs spreadsheet files"
// Author: "Geoplex"
// 
#endregion

#region (c) Copyright 2014 Geoplex
//
// THE SOFTWARE IS PROVIDED "AS-IS" AND WITHOUT WARRANTY OF ANY KIND,
// EXPRESS, IMPLIED OR OTHERWISE, INCLUDING WITHOUT LIMITATION, ANY
// WARRANTY OF MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE.
//
// IN NO EVENT SHALL GEOPLEX BE LIABLE FOR ANY SPECIAL, INCIDENTAL,
// INDIRECT OR CONSEQUENTIAL DAMAGES OF ANY KIND, OR ANY DAMAGES WHATSOEVER
// RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER OR NOT ADVISED OF THE
// POSSIBILITY OF DAMAGE, AND ON ANY THEORY OF LIABILITY, ARISING OUT OF OR IN
// CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
//
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OpenXmlUtils
{
    public class Spreadsheet
    {
        /// <summary>
        /// Write xlsx spreadsheet file of a list of T objects
        /// Maximum of 24 columns
        /// </summary>
        /// <typeparam name="T">Type of objects passed in</typeparam>
        /// <param name="fileName">Full path filename for the new spreadsheet</param>
        /// <param name="def">A sheet definition used to create the spreadsheet</param>
        public static void Create<T>(
            string fileName,
            SheetDefinition<T> def)
        {
            // open a template workbook
            using (var myWorkbook = SpreadsheetDocument.Create(fileName, SpreadsheetDocumentType.Workbook))
            {
                // create workbook part
                var workbookPart = myWorkbook.AddWorkbookPart();

                // add stylesheet to workbook part
                var stylesPart = myWorkbook.WorkbookPart.AddNewPart<WorkbookStylesPart>();
                Stylesheet styles = new CustomStylesheet();
                styles.Save(stylesPart);

                // create workbook
                var workbook = new Workbook();

                // add work sheet
                var sheets = new Sheets();
                sheets.AppendChild(CreateSheet(def, workbookPart));
                workbook.AppendChild(sheets);

                // add workbook to workbook part
                myWorkbook.WorkbookPart.Workbook = workbook;
                myWorkbook.WorkbookPart.Workbook.Save();
                myWorkbook.Close();
            }
        }

        /// <summary>
        /// Write xlsx spreadsheet file of a list of T objects
        /// Maximum of 24 columns
        /// </summary>
        /// <typeparam name="T">Type of objects passed in</typeparam>
        /// <param name="fileName">Full path filename for the new spreadsheet</param>
        /// <param name="defs">A list of sheet definitions used to create the spreadsheet</param>
        public static void Create<T>(
            string fileName,
            IEnumerable<SheetDefinition<T>> defs)
        {
            // open a template workbook
            using (var myWorkbook = SpreadsheetDocument.Create(fileName, SpreadsheetDocumentType.Workbook))
            {
                // create workbook part
                var workbookPart = myWorkbook.AddWorkbookPart();

                // add stylesheet to workbook part
                var stylesPart = myWorkbook.WorkbookPart.AddNewPart<WorkbookStylesPart>();
                Stylesheet styles = new CustomStylesheet();
                styles.Save(stylesPart);

                // create workbook
                var workbook = new Workbook();

                // add work sheets
                var sheets = new Sheets();
                foreach (var def in defs)
                {
                    sheets.AppendChild(CreateSheet(def, workbookPart));
                }
                workbook.AppendChild(sheets);

                // add workbook to workbook part
                myWorkbook.WorkbookPart.Workbook = workbook;
                myWorkbook.WorkbookPart.Workbook.Save();
                myWorkbook.Close();
            }
        }

        private static Sheet CreateSheet<T>(SheetDefinition<T> def, WorkbookPart workbookPart)
        {
            // create worksheet part
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var worksheetId = workbookPart.GetIdOfPart(worksheetPart);

            // variables
            var numCols = def.Fields.Count;
            var numRows = def.Objects.Count;
            var az = new List<Char>(Enumerable.Range('A', 'Z' - 'A' + 1).Select(i => (Char) i).ToArray());
            var headerCols = az.GetRange(0, numCols);
            var hasTitleRow = def.Title != null;
            var hasSubtitleRow = def.SubTitle != null;
            var titleRowCount = hasTitleRow ? 1 + (hasSubtitleRow ? 1 : 0) : hasSubtitleRow ? 1 : 0;

            // get the worksheet data
            int firstTableRow;
            var sheetData = CreateSheetData(def.Objects, def.Fields, headerCols, def.IncludeTotalsRow, def.Title,
                def.SubTitle,
                out firstTableRow);

            // populate column metadata
            var columns = new Columns();
            for (var col = 0; col < numCols; col++)
            {
                var width = ColumnWidth(sheetData, col, titleRowCount);
                columns.AppendChild(CreateColumnMetadata((UInt32) col + 1, (UInt32) numCols + 1, width));
            }

            // populate worksheet
            var worksheet = new Worksheet();
            worksheet.AppendChild(columns);
            worksheet.AppendChild(sheetData);

            // add an auto filter
            worksheet.AppendChild(new AutoFilter
            {
                Reference =
                    String.Format("{0}{1}:{2}{3}", headerCols.First(), firstTableRow - 1, headerCols.Last(),
                        numRows + titleRowCount + 1)
            });

            // add worksheet to worksheet part
            worksheetPart.Worksheet = worksheet;
            worksheetPart.Worksheet.Save();

            return new Sheet {Name = def.Name, SheetId = 1, Id = worksheetId};
        }

        private static double ColumnWidth(SheetData sheetData, int col, int titleRowCount)
        {
            var rows = sheetData.ChildElements.ToList();
            if (col == 0)
            {
                rows = sheetData.ChildElements.ToList().GetRange(titleRowCount, sheetData.ChildElements.Count - titleRowCount);
            }

            var maxLength = (from row in rows
                where row.ChildElements.Count > col
                select row.ChildElements[col]
                into cell
                where cell.GetType() != typeof (FormulaCell)
                select cell.InnerText.Length).Concat(new[] {0}).Max();
            var width = maxLength*0.9 + 5;
            return width;
        }

        private static SheetData CreateSheetData<T>(IList< T> objects, List<SpreadsheetField> fields,
            List<char> headerCols, bool includedTotalsRow, string sheetTitle, string sheetSubTitle,
            out int firstTableRow)
        {
            var sheetData = new SheetData();
            var fieldNames = fields.Select(f => f.Title).ToList();
            var numCols = headerCols.Count;
            var rowIndex = 0;
            firstTableRow = 0;
            Row row;

            // create title
            if (sheetTitle != null)
            {
                rowIndex++;
                row = CreateTitle(sheetTitle, headerCols, ref rowIndex);
                sheetData.AppendChild(row);
            }

            // create subtitle
            if (sheetSubTitle != null)
            {
                rowIndex++;
                row = CreateSubTitle(sheetSubTitle, headerCols, ref rowIndex);
                sheetData.AppendChild(row);
            }

            // create the header
            rowIndex++;
            row = CreateHeader(fieldNames, headerCols, ref rowIndex);
            sheetData.AppendChild(row);

            if (objects.Count == 0)
                return sheetData;

            // create a row for each object and set the columns for each field
            firstTableRow = rowIndex + 1;
            CreateTable(objects, ref rowIndex, numCols, fields, headerCols, sheetData);

            // create an additional row with summed totals
            if (includedTotalsRow)
            {
                rowIndex++;
                AppendTotalsRow(objects, rowIndex, firstTableRow, numCols, fields, headerCols, sheetData);
            }

            return sheetData;
        }

        private static Row CreateTitle(string title, List<char> headerCols, ref int rowIndex)
        {
            var header = new Row {RowIndex = (uint) rowIndex, Height = 40, CustomHeight = true};
            var c = new TextCell(headerCols[0].ToString(), title, rowIndex)
            {
                StyleIndex = (UInt32)CustomStylesheet.CustomCellFormats.TitleText
            };
            header.Append(c);

            return header;
        }

        private static Row CreateSubTitle(string title, List<char> headerCols, ref int rowIndex)
        {
            var header = new Row { RowIndex = (uint)rowIndex, Height = 28, CustomHeight = true };

            var c = new TextCell(headerCols[0].ToString(), title, rowIndex)
            {
                StyleIndex = (UInt32)CustomStylesheet.CustomCellFormats.SubtitleText
            };
            header.Append(c);

            return header;
        }

        private static Row CreateHeader(IList<string> headerNames, List<char> headerCols, ref int rowIndex)
        {
            var header = new Row {RowIndex = (uint) rowIndex};

            for (var col = 0; col < headerCols.Count; col++)
            {
                var c = new TextCell(headerCols[col].ToString(), headerNames[col], rowIndex)
                {
                    StyleIndex = (UInt32) CustomStylesheet.CustomCellFormats.HeaderText
                };
                header.Append(c);
            }
            return header;
        }

        private static void CreateTable<T>(IList<T> objects, ref int rowIndex, int numCols,
            List<SpreadsheetField> fields, List<char> headers, SheetData sheetData)
        {
            var fieldNames = fields.Select(f => f.FieldName).ToList();

            // for each object
            foreach (var rowObj in objects)
            {
                rowIndex++;

                // create a row
                var r = new Row {RowIndex = (uint) rowIndex};
                int col;

                // populate columns using supplied objects
                for (col = 0; col < numCols; col++)
                {
                    var columnObj = GetColumnObject(fieldNames[col], rowObj);
                    if (columnObj == null || columnObj == DBNull.Value) continue;

                    if (columnObj is string)
                    {
                        var c = new TextCell(headers[col].ToString(), columnObj.ToString(), rowIndex);
                        r.AppendChild(c);
                    }
                    else if (columnObj is bool)
                    {
                        var value = (bool) columnObj ? "Yes" : "No";
                        var c = new TextCell(headers[col].ToString(), value, rowIndex);
                        r.AppendChild(c);
                    }
                    else if (columnObj is DateTime)
                    {
                        var c = new DateCell(headers[col].ToString(), (DateTime) columnObj, rowIndex);
                        r.AppendChild(c);
                    }
                    else if (columnObj is TimeSpan)
                    {
                        var ts = (TimeSpan) columnObj;
                        // excel stores time as "fraction of hours in a day"
                        var c = new NumberCell(headers[col].ToString(), (ts.TotalHours / 24).ToString(), rowIndex)
                        {
                            StyleIndex = (UInt32)CustomStylesheet.CustomCellFormats.Duration
                        };
                        r.AppendChild(c);
                    }
                    else if (columnObj is decimal || columnObj is double)
                    {
                        var c = new NumberCell(headers[col].ToString(), columnObj.ToString(), rowIndex)
                        {
                            StyleIndex = (UInt32) CustomStylesheet.CustomCellFormats.DefaultNumber2DecimalPlace
                        };
                        r.AppendChild(c);
                    }
                    else
                    {
                        long value;
                        if (long.TryParse(columnObj.ToString(), out value))
                        {
                            var c = new NumberCell(headers[col].ToString(), columnObj.ToString(), rowIndex);
                            r.AppendChild(c);
                        }
                        else
                        {
                            var c = new TextCell(headers[col].ToString(), columnObj.ToString(), rowIndex);

                            r.AppendChild(c);
                        }
                    }
                } // for each column

                sheetData.AppendChild(r);
            }
        }

        private static object GetColumnObject<T>(string fieldName, T rowObj)
        {
            // is the object a dictionary?
            if (IsStringObjectDictionary(rowObj))
            {
                var dict = (IDictionary<string, object>) rowObj;
                return !dict.ContainsKey(fieldName) ? null : dict[fieldName];
            }

            // get the properties for this object type
            var properties = GetPropertyInfo<T>();
            if (!properties.Contains(fieldName))
                return null;

            var myf = rowObj.GetType().GetProperty(fieldName);
            if (myf == null) 
                return null;

            var obj = myf.GetValue(rowObj, null);
            return obj;
        }

        private static void AppendTotalsRow<T>(IList<T> objects, int rowIndex, int firstTableRow, int numCols,
            List<SpreadsheetField> fields,
            List<char> headers,
            SheetData sheetData)
        {
            var fieldNames = fields.Select(f => f.FieldName).ToList();
            var rowObj = objects[0];
            var total = new Row {RowIndex = (uint) rowIndex};

            for (var col = 0; col < numCols; col++)
            {
                var field = fields[col];
                if (field.IgnoreFromTotals)
                {
                    total.AppendChild(new TextCell(headers[col].ToString(), string.Empty, rowIndex)
                    {
                        StyleIndex = (UInt32)CustomStylesheet.CustomCellFormats.TotalsText
                    });
                    continue;
                }

                var columnObject = GetColumnObject(fieldNames[col], rowObj);

                // look through objects until we have a value for this column
                var row = 0;
                while (columnObject == null || columnObject == DBNull.Value)
                {
                    if (objects.Count <= ++row)
                        break;
                    columnObject = GetColumnObject(fieldNames[col], objects[row]);
                }

                if (field.CountNoneNullRowsForTotal)
                {
                    total.AppendChild(CreateRowTotalFomulaCell(rowIndex, firstTableRow, headers, col, 
                        (UInt32)CustomStylesheet.CustomCellFormats.TotalsNumber, true));
                }

                if (col == 0)
                {
                    total.AppendChild(new TextCell(headers[col].ToString(), "Total", rowIndex)
                    {
                        StyleIndex = (UInt32) CustomStylesheet.CustomCellFormats.TotalsText
                    });
                }
                else if (columnObject is decimal || columnObject is double)
                {
                    total.AppendChild(CreateRowTotalFomulaCell(rowIndex, firstTableRow, headers, col,
                        (UInt32) CustomStylesheet.CustomCellFormats.TotalsNumber2DecimalPlace));
                }
                else if (columnObject is TimeSpan)
                {
                    total.AppendChild(CreateRowTotalFomulaCell(rowIndex, firstTableRow, headers, col,
                           (UInt32)CustomStylesheet.CustomCellFormats.TotalsDuration));
                }
                else
                {
                    long value;
                    if (columnObject != null &&
                        long.TryParse(columnObject.ToString(), out value))
                    {
                        total.AppendChild(CreateRowTotalFomulaCell(rowIndex, firstTableRow, headers, col,
                            (UInt32) CustomStylesheet.CustomCellFormats.TotalsNumber));
                    }
                    else
                    {
                        total.AppendChild(new TextCell(headers[col].ToString(), string.Empty, rowIndex)
                        {
                            StyleIndex = (UInt32) CustomStylesheet.CustomCellFormats.TotalsText
                        });
                    }
                }
            } // for each column
            sheetData.AppendChild(total);
        }

        private static FormulaCell CreateRowTotalFomulaCell(int rowIndex, int firstTableRow, List<char> headers, int col, UInt32 styleIndex, bool countNonBlank = false)
        {
            var headerCol = headers[col].ToString();
            var firstRow = headerCol + firstTableRow;
            var lastRow = headerCol + (rowIndex - 1);
            return CreateFormulaCell(rowIndex, headers, col, styleIndex, countNonBlank, firstRow, lastRow);
        }

        private static FormulaCell CreateFormulaCell(int rowIndex, List<char> headers, int col, uint styleIndex,
            bool countNonBlank, string firstCell, string lastCell)
        {
            var formula = (countNonBlank ? "COUNTA" : "SUM") + "(" + firstCell + ":" + lastCell + ")";
            return new FormulaCell(headers[col].ToString(), formula, rowIndex) {StyleIndex = styleIndex};
        }

        private static List<string> GetPropertyInfo<T>()
        {
            var propertyInfos = typeof (T).GetProperties();
            return propertyInfos.Select(propertyInfo => propertyInfo.Name).ToList();
        }

        private static Column CreateColumnMetadata(UInt32 startColumnIndex, UInt32 endColumnIndex, double width)
        {
            var column = new Column
            {
                Min = startColumnIndex,
                Max = endColumnIndex,
                BestFit = true,
                Width = width,
            };
            return column;
        }

        private static bool IsStringObjectDictionary(object dict)
        {
            var t = dict.GetType();
            var isDict = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>);
            if (!isDict)
                return false;

            var keyType = t.GetGenericArguments()[0];
            var valueType = t.GetGenericArguments()[1];
            return keyType == typeof (String) &&
                   valueType == typeof (Object);
        }
    }
}