using System.ComponentModel;

namespace AccC3DMetadata.Models
{
    /// <summary>
    /// One editable row in the <see cref="UI.ConfigEditorDialog"/> attribute table — binds a
    /// single block attribute tag to an ACC custom attribute name and sync behaviour.
    /// </summary>
    public class ConfigEditorRow : INotifyPropertyChanged
    {
        private bool _use;
        private string _formaName;
        private SyncDirection _direction = SyncDirection.ReadWrite;
        private ConflictStrategy _conflictStrategy = ConflictStrategy.Prompt;

        /// <summary>The block attribute tag this row represents — fixed by the drawing, not editable.</summary>
        public string AttributeName { get; init; }

        /// <summary>Whether this attribute is included in the saved config.</summary>
        public bool Use
        {
            get => _use;
            set
            {
                _use = value;
                OnPropertyChanged(nameof(Use));
            }
        }

        /// <summary>The ACC custom attribute name to map to. Defaults to <see cref="AttributeName"/>.</summary>
        public string FormaName
        {
            get => _formaName;
            set
            {
                _formaName = value;
                OnPropertyChanged(nameof(FormaName));
            }
        }

        public SyncDirection Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                OnPropertyChanged(nameof(Direction));
            }
        }

        public ConflictStrategy ConflictStrategy
        {
            get => _conflictStrategy;
            set
            {
                _conflictStrategy = value;
                OnPropertyChanged(nameof(ConflictStrategy));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
