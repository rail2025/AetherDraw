// Create this file as: DrawingLogic/InputSanitizer.cs
using System.Text.RegularExpressions;
// Assuming Plugin.Log is accessible.
// using static YourPluginNamespace.Plugin; 

namespace AetherDraw.DrawingLogic
{
    public static class InputSanitizer
    {
        // A regex to match most C0 and C1 control characters, plus DEL, but keep common whitespace like tab, LF, CR.
        // \u0000-\u0008 (NULL to BS)
        // \u000B-\u000C (VT, FF)
        // \u000E-\u001F (SO to US)
        // \u007F (DEL)
        // \u0080-\u009F (C1 control characters)
        private static readonly Regex ControlCharRegex = new Regex(@"[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F-\u009F]", RegexOptions.Compiled);

        /// <summary>
        /// Sanitizes text input to prevent unintended ImGui formatting issues or remove unwanted characters.
        /// </summary>
        /// <param name="inputText">The text to sanitize.</param>
        /// <returns>The sanitized text. Returns an empty string if input is null or empty.</returns>
        public static string Sanitize(string inputText)
        {
            if (string.IsNullOrEmpty(inputText))
            {
                AetherDraw.Plugin.Log?.Debug("[InputSanitizer] Sanitize: Input is null or empty, returning empty string.");
                return string.Empty;
            }

            string originalTextForLog = inputText.Length > 30 ? inputText.Substring(0, 30) + "..." : inputText;
            // AetherDraw.Plugin.Log?.Debug($"[InputSanitizer] Sanitize: Starting sanitization for: \"{originalTextForLog}\"");

            // 1. Prevent ImGui's '%%' from being interpreted as a single '%' if user types it literally.
            // This also prevents "%%c" or "%%[color_hex]" from being parsed as color codes by ImGui itself.
            // Note: If you *want* users to be able to use ImGui's color tags (e.g., "%%[FF0000]Red%%"),
            // then this specific replacement should be more nuanced or removed.
            // For a general drawing tool, it's usually safer to escape or disallow them.
            string sanitizedText = inputText.Replace("%%", "% %"); // Escapes '%%' to be rendered as literal "%%" (actually renders as "% %")
                                                                   // If you want to display "%%", you might need "%%%%".
                                                                   // A simple way to just prevent %% from being special is to break it.

            // 2. Remove most control characters (except tab, newline, carriage return if they are desired)
            // The regex above is defined to do this.
            // sanitizedText = ControlCharRegex.Replace(sanitizedText, "");

            // 3. (Optional) Normalize line endings if needed (e.g., all \r\n or \r to \n)
            // For ImGui.InputTextMultiline, it usually handles line endings fairly well, typically using \n internally.
            // This step is often not strictly necessary for display with ImGui but can be for consistency if saving/loading.
            // sanitizedText = sanitizedText.Replace("\r\n", "\n").Replace("\r", "\n");


            if (inputText != sanitizedText)
            {
                AetherDraw.Plugin.Log?.Debug($"[InputSanitizer] Sanitize: Text was modified. Original: \"{originalTextForLog}\", Sanitized: \"{(sanitizedText.Length > 30 ? sanitizedText.Substring(0, 30) + "..." : sanitizedText)}\"");
            }
            else
            {
                // AetherDraw.Plugin.Log?.Debug($"[InputSanitizer] Sanitize: Text unchanged: \"{originalTextForLog}\"");
            }
            return sanitizedText;
        }
    }
}
