using System;
using System.IO;

namespace slot
{
    internal sealed class ConsoleCanvas
    {
        private readonly char[] _characters;
        private readonly ConsoleColor[] _foreground;
        private readonly ConsoleColor[] _background;
        public int Width { get; }
        public int Height { get; }

        public ConsoleCanvas(int width, int height)
        {
            Width = Math.Max(1, width);
            Height = Math.Max(1, height);
            _characters = new string(' ', Width * Height).ToCharArray();
            _foreground = new ConsoleColor[_characters.Length];
            _background = new ConsoleColor[_characters.Length];
        }

        public void Write(int x, int y, string text, ConsoleColor color = ConsoleColor.Gray, ConsoleColor background = ConsoleColor.Black)
        {
            if (y < 0 || y >= Height || text == null) return;
            for (int i = 0; i < text.Length; i++)
            {
                int column = x + i;
                if (column < 0 || column >= Width) continue;
                int index = y * Width + column;
                _characters[index] = char.IsControl(text[i]) ? ' ' : text[i];
                _foreground[index] = color;
                _background[index] = background;
            }
        }

        public void Center(int x, int y, int width, string text, ConsoleColor color = ConsoleColor.Gray, ConsoleColor background = ConsoleColor.Black)
        {
            text = Fit(text, width);
            Write(x + Math.Max(0, (width - text.Length) / 2), y, text, color, background);
        }

        public void Box(int x, int y, int width, int height, string title, ConsoleColor color = ConsoleColor.DarkGray)
        {
            if (width < 2 || height < 2) return;
            Write(x, y, "┌" + new string('─', width - 2) + "┐", color);
            for (int row = 1; row < height - 1; row++)
            {
                Write(x, y + row, "│", color);
                Write(x + width - 1, y + row, "│", color);
            }
            Write(x, y + height - 1, "└" + new string('─', width - 2) + "┘", color);
            if (!string.IsNullOrEmpty(title)) Write(x + 2, y, Fit(" " + title + " ", width - 4), color);
        }

        public static string Fit(string text, int width)
        {
            if (width <= 0) return "";
            text = text ?? "";
            return text.Length <= width ? text : width > 3 ? text.Substring(0, width - 3) + "..." : text.Substring(0, width);
        }

        public bool Present(ConsoleCanvas previous)
        {
            try
            {
                bool sameSize = previous != null && previous.Width == Width && previous.Height == Height;
                for (int y = 0; y < Height; y++)
                {
                    int x = 0;
                    while (x < Width)
                    {
                        int start = y * Width + x;
                        int end = start + 1;
                        while (end < (y + 1) * Width && _foreground[end] == _foreground[start] && _background[end] == _background[start]) end++;
                        bool changed = !sameSize;
                        for (int i = start; !changed && i < end; i++)
                            changed = _characters[i] != previous._characters[i] || _foreground[i] != previous._foreground[i] || _background[i] != previous._background[i];
                        if (changed)
                        {
                            Console.SetCursorPosition(Console.WindowLeft + x, Console.WindowTop + y);
                            Console.ForegroundColor = _foreground[start];
                            Console.BackgroundColor = _background[start];
                            Console.Write(new string(_characters, start, end - start));
                        }
                        x += end - start;
                    }
                }
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
