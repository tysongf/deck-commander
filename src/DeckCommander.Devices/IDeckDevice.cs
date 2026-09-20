using System;

namespace DeckCommander.Devices
{
    /// <summary>
    /// Everything <see cref="DeckCommander.Core.DeckController"/> needs from a deck. Implemented
    /// by <see cref="AjazzStreamDock"/> for real hardware and by <see cref="VirtualDeck"/>
    /// for an on-screen one, so the controller, the renderer and the profile format do not
    /// know or care which is in front of them.
    /// </summary>
    /// <remarks>
    /// This is also the seam a second protocol plugs into. An Elgato Stream Deck speaks a
    /// different wire protocol but has the same shape of behaviour, so it belongs here as a
    /// third implementation rather than as a branch inside the transport.
    /// </remarks>
    public interface IDeckDevice : IDisposable
    {
        /// <summary>Geometry, image format and key maps for this device.</summary>
        DeckModel Model { get; }

        /// <summary>One line naming the device, for a status bar.</summary>
        string Description { get; }

        /// <summary>Raised for every key transition.</summary>
        event EventHandler<DeckKeyEventArgs> KeyChanged;

        /// <summary>Raised when a knob is turned or pressed. Knob decks only.</summary>
        event EventHandler<DeckEncoderEventArgs> EncoderChanged;

        /// <summary>Brings the device to a state where it accepts images and reports input.</summary>
        void Initialize(int brightness);

        /// <summary>Begins delivering input.</summary>
        void Start();

        /// <summary>Stops delivering input.</summary>
        void Stop();

        void SetBrightness(int percent);

        /// <summary>Uploads a JPEG to a key by row-major grid index.</summary>
        void SetKeyImageJpeg(int grid, byte[] jpeg);

        /// <summary>Uploads a JPEG to a knob screen.</summary>
        void SetEncoderImageJpeg(int encoder, byte[] jpeg);

        void ClearKey(int grid);
        void ClearAll();

        /// <summary>Makes pending image writes visible.</summary>
        void Commit();
    }
}
