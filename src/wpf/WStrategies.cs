// Стратегии: избранные, заметки, быстрый подбор, сравнение двух, страница «Стратегии».
using System;
using System.Collections.Generic;
using System.Linq;

namespace ZapretGui
{
    partial class MainWindow
    {
        // Избранные стратегии (звёздочки): стрелки на панели листают только их, автовосстановление пробует их первыми.
        static List<string> Favorites
        {
            get { return Prefs.Get("Favorites", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
            set { Prefs.Set("Favorites", string.Join("|", value)); }
        }
    }
}
