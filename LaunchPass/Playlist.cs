﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace RetroPass
{
    public class Playlist : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyyChanged(string propertyname)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyname));
            }
        }
        public string Name { get; set; }

        public BitmapImage Thumbnail { get; set; }
        private ObservableCollection<PlaylistItem> playlistItems = new ObservableCollection<PlaylistItem>();
        public ObservableCollection<PlaylistItem> PlaylistItems
        {
            get
            {
                return playlistItems;
            }
            set
            {
                playlistItems = value;
                OnPropertyyChanged("PlaylistItems");
            }
        }
        public ObservableCollection<PlaylistItem> PlaylistItemsLandingPage { get; set; } = new ObservableCollection<PlaylistItem>();

        public PlaylistItem AddPlaylistItem(Game game)
        {
            PlaylistItem playlistItem = new PlaylistItem();
            playlistItem.playlist = this;
            playlistItem.game = game;

            if (PlaylistItems.Any(p => p.game.Title == game.Title))
            {
                var playlistItem1 = PlaylistItems.FirstOrDefault(p => p.game.Title == game.Title);
                PlaylistItems.Remove(playlistItem1);
            }
            PlaylistItems.Add(playlistItem);

            return playlistItem;
        }

        public void ClearLastPlayedSettings()
        {
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values["LastPlayed" + Name] = "";
        }

        public void UpdateGamesLandingPage()
        {
            //ClearLandingPage();

            PlaylistItemsLandingPage.Clear();

            List<PlaylistItem> games = PlaylistItems.Take(5).ToList();
            List<PlaylistItem> lastPlayedGames = new List<PlaylistItem>();
            //check if there are some already played
            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;

            string lastPlayedStr = (string)localSettings.Values["LastPlayed" + Name];

            if (string.IsNullOrEmpty(lastPlayedStr) == false)
            {
                string[] lastPlayed = lastPlayedStr.Split(";;;");

                foreach (var title in lastPlayed)
                {
                    PlaylistItem game = PlaylistItems.FirstOrDefault(t => t.game.Title == title);
                    if (game != null)
                    {
                        lastPlayedGames.Add(game);
                    }
                }
            }

            games = lastPlayedGames.Concat(games).Distinct().Take(5).ToList();

            for (int i = 0; i < games.Count; i++)
            {
                //add item if there is none
                PlaylistItemsLandingPage.Add(games[i]);
            }
        }

        public void SetLastPlayed(PlaylistItem playlist)
        {
            List<string> lastPlayedGames = new List<string>();

            ApplicationDataContainer localSettings = ApplicationData.Current.LocalSettings;
            string lastPlayedStr = (string)localSettings.Values["LastPlayed" + Name];

            if (string.IsNullOrEmpty(lastPlayedStr) == false)
            {
                lastPlayedGames.AddRange(lastPlayedStr.Split(";;;"));
                lastPlayedGames.Insert(0, playlist.game.Title);
                lastPlayedGames = lastPlayedGames.Distinct().Take(5).ToList();
            }
            else
            {
                lastPlayedGames.Insert(0, playlist.game.Title);
            }

            lastPlayedStr = string.Join(";;;", lastPlayedGames.ToArray());
            localSettings.Values["LastPlayed" + Name] = lastPlayedStr;

            UpdateGamesLandingPage();
        }

        public void Sort()
        {
            List<PlaylistItem> sorted = PlaylistItems.OrderBy(t => t.game.Title).ToList();
            for (int i = 0; i < sorted.Count(); i++)
            {
                PlaylistItems.Move(PlaylistItems.IndexOf(sorted[i]), i);
            }
        }
    }
}