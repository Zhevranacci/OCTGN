﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

namespace Octgn.Play
{
    public sealed class Player : INotifyPropertyChanged
    {
        #region Static members

        // Contains all players in this game (TODO: Rename to All, then cleanup all the dependancies)
        private static readonly ObservableCollection<Player> all = new ObservableCollection<Player>();
        public static Player LocalPlayer;
        // May be null if there's no global lPlayer in the game definition
        public static Player GlobalPlayer;

        // Get all players in the game
        public static IEnumerable<Player> All
        {
            get { return all; }
        }

        // Get all players in the game, except a possible Global lPlayer
        public static IEnumerable<Player> AllExceptGlobal
        {
            get { return All.Where(p => p != GlobalPlayer); }
        }

        // Number of players
        internal static int Count
        {
            get { return GlobalPlayer == null ? all.Count : all.Count - 1; }
        }

        // Find a lPlayer with his id
        internal static Player Find(byte id)
        {
            return all.FirstOrDefault(p => p.Id == id);
        }

        // Resets the lPlayer list
        internal static void Reset()
        {
            all.Clear();
            LocalPlayer = GlobalPlayer = null;
        }

        public static event Action OnLocalPlayerWelcomed;
        public static void FireLocalPlayerWelcomed()
        {
            if(OnLocalPlayerWelcomed != null)
                OnLocalPlayerWelcomed.Invoke();
        }

        // May be null if we're in pure server mode

        internal static event EventHandler<PlayerEventArgs> PlayerAdded;
        internal static event EventHandler<PlayerEventArgs> PlayerRemoved;

        #endregion

        #region Public fields and properties

        internal readonly ulong PublicKey; // Public cryptographic key
        private readonly Counter[] _counters; // Counters this lPlayer owns

        private readonly Group[] _groups; // Groups this lPlayer owns
        private readonly Hand _hand; // Hand of this lPlayer (may be null)
        private Brush _solidBrush;
        private Brush _transparentBrush;
        private bool _invertedTable;
        private string _name;
        private byte _id;

        public Counter[] Counters
        {
            get { return _counters; }
        }

        public Group[] IndexedGroups
        {
            get { return _groups; }
        }

        public IEnumerable<Group> Groups
        {
            get { return _groups.Where(g => g != null); }
        }

        public Dictionary<string, int> Variables { get; private set; }
        public Dictionary<string, string> GlobalVariables { get; private set; }

        public Hand Hand
        {
            get { return _hand; }
        }

        public byte Id // Identifier
        {
            get { return _id; }
            set
            {
                if (_id == value) return;
                _id = value;
                OnPropertyChanged("Id");
            }
        }

        public string Name // Nickname
        {
            get { return _name; }
            set
            {
                if (_name == value) return;
                _name = value;
                OnPropertyChanged("Name");
            }
        }

        public bool IsGlobalPlayer
        {
            get { return Id == 0; }
        }

        public bool InvertedTable
            // True if the lPlayer plays on the opposite side of the table (for two-sided table only)
        {
            get { return _invertedTable; }
            set
            {
                if (_invertedTable == value) return;
                _invertedTable = value;
                OnPropertyChanged("InvertedTable");

                if (Program.IsHost) // If we are the host, we are setting this option for everyone
                    Program.Client.Rpc.PlayerSettings(this, value);
            }
        }


        //Color for the chat.
        // Associated color
        public Color Color { get; set; }

        // Work around a WPF binding bug ? Borders don't seem to bind correctly to Color!
        public Brush Brush
        {
            get { return _solidBrush; }
            set { _solidBrush = value; }
        }

        public Brush TransparentBrush
        {
            get { return _transparentBrush; }
            set { _transparentBrush = value; }
        }

        //Set the player's color based on their id.
        public void SetPlayerColor(int idx)
        {
            // Create the Player's Color
            Color[] baseColors = {
                                     Color.FromRgb(0x00, 0x00, 0x00),
                                     Color.FromRgb(0x00, 0x66, 0x00),
                                     Color.FromRgb(0x66, 0x00, 0x00),
                                     Color.FromRgb(0x00, 0x00, 0x66),
                                     Color.FromRgb(0x66, 0x00, 0x66),
                                     Color.FromRgb(0xFF, 0x66, 0x00),
                                     Color.FromRgb(0x00, 0x00, 0x00),
                                     Color.FromRgb(0x00, 0x99, 0x00),
                                     Color.FromRgb(0x99, 0x00, 0x00),
                                     Color.FromRgb(0x00, 0x00, 0x99),
                                     Color.FromRgb(0x99, 0x00, 0x99),
                                     Color.FromRgb(0xFF, 0x99, 0x00),
                                     Color.FromRgb(0x33, 0x33, 0x33),
                                     Color.FromRgb(0x00, 0x99, 0x00),
                                     Color.FromRgb(0x99, 0x00, 0x00),
                                     Color.FromRgb(0x00, 0x00, 0x99),
                                     Color.FromRgb(0x99, 0x00, 0x99),
                                     Color.FromRgb(0xFF, 0x99, 0x00),
                                     Color.FromRgb(0x66, 0x66, 0x66),
                                     Color.FromRgb(0xFF, 0x00, 0x00)
                                 };
            if (idx == 255)
                idx = 0;
            if (idx > 18)
                idx = idx - 18;
            Color = baseColors[idx];
            _solidBrush = new SolidColorBrush(Color);
            _solidBrush.Freeze();
            _transparentBrush = new SolidColorBrush(Color) {Opacity = 0.4};
            _transparentBrush.Freeze();

            //Notify clients that this has changed
            OnPropertyChanged("Color");
            OnPropertyChanged("Brush");
            OnPropertyChanged("TransparentBrush");
        }

        #endregion

        #region Public interface

        // C'tor
        internal Player(DataNew.Entities.Game g, string name, byte id, ulong pkey)
        {
            // Init fields
            _name = name;
            Id = id;
            PublicKey = pkey;
            // Register the lPlayer
            all.Add(this);
            //Create the color brushes           
            SetPlayerColor(id);
            // Create counters
            _counters = new Counter[0];
            if (g.Player.Counters != null)
                _counters = g.Player.Counters.Select(x =>new Counter(this, x) ).ToArray();
            // Create variables
            Variables = new Dictionary<string, int>();
            foreach (var varDef in g.Variables.Where(v => !v.Global))
                Variables.Add(varDef.Name, varDef.Default);
            // Create global variables
            GlobalVariables = new Dictionary<string, string>();
            foreach (var varD in g.Player.GlobalVariables)
                GlobalVariables.Add(varD.Name, varD.Value);
            // Create a hand, if any
            if (g.Player.Hand != null)
                _hand = new Hand(this, g.Player.Hand);
            // Create groups
            _groups = new Group[0];
            if (g.Player.Groups != null)
            {
                var tempGroups = g.Player.Groups.ToArray();
                _groups = new Group[tempGroups.Length + 1];
                _groups[0] = _hand;
                for (int i = 1; i < IndexedGroups.Length; i++)
                    _groups[i] = new Pile(this, tempGroups[i - 1]);
            }
            // Raise the event
            if (PlayerAdded != null) PlayerAdded(null, new PlayerEventArgs(this));
        }

        // C'tor for global items
        internal Player(DataNew.Entities.Game g)
        {
            var globalDef = g.GlobalPlayer;
            // Register the lPlayer
            all.Add(this);
            // Init fields
            _name = "Global";
            Id = 0;
            PublicKey = 0;
            if (GlobalVariables == null)
            {
                // Create global variables
                GlobalVariables = new Dictionary<string, string>();
                foreach (var varD in g.Player.GlobalVariables)
                    GlobalVariables.Add(varD.Name, varD.Value);
            }
            // Create counters
            _counters = new Counter[0];
            if (globalDef.Counters != null)
                _counters = globalDef.Counters.Select(x => new Counter(this, x)).ToArray();
            // Create global's lPlayer groups
            // TODO: This could fail with a run-time exception on write, make it safe
            // I don't know if the above todo is still relevent - Kelly Elton - 3/18/2013
            if (globalDef.Groups != null)
            {
                var tempGroups = globalDef.Groups.ToArray();
                _groups = new Group[tempGroups.Length + 1];
                _groups[0] = _hand;
                for (int i = 1; i < IndexedGroups.Length; i++)
                    _groups[i] = new Pile(this, tempGroups[i - 1]);
            }
        }

        // Remove the lPlayer from the game
        internal void Delete()
        {
            // Remove from the list
            all.Remove(this);
            // Raise the event
            if (PlayerRemoved != null) PlayerRemoved(null, new PlayerEventArgs(this));
        }

        public override string ToString()
        {
            return _name;
        }

        #endregion

        #region INotifyPropertyChanged Members

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        private void OnPropertyChanged(string property)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(property));
        }
    }

    public class PlayerEventArgs : EventArgs
    {
        public readonly Player Player;

        public PlayerEventArgs(Player p)
        {
            Player = p;
        }
    }
}