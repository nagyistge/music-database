﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MusicLib
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            libdb.Database.Open("music.db3");

            //Application.Run(new BrowseMusic());
            Application.Run(new AddAlbum());
            //Application.Run(new SelectMultipleArtists());
        }
    }
}
