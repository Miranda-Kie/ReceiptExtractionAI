using UglyToad.PdfPig;
var path = args[0];
using var doc = PdfDocument.Open(path);
Console.WriteLine($"Pages: {doc.NumberOfPages}");
var i = 0;
foreach (var page in doc.GetPages())
{
    i++;
    var t = page.Text ?? "";
    var preview = t.Length <= 200 ? t : t.Substring(0, 200);
    preview = preview.Replace("\r", " ").Replace("\n", " ");
    Console.WriteLine($"Page {i}: textLen={t.Length} words={page.GetWords().Count()} preview=[{preview}]");
}
