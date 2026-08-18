namespace LightningForge.Chess.Core
{
    public enum PieceType : byte
    {
        None = 0,
        Pawn = 1,
        Knight = 2,
        Bishop = 3,
        Rook = 4,
        Queen = 5,
        King = 6
    }

    public enum PieceColor : byte
    {
        White = 0,
        Black = 1
    }

    /// <summary>
    /// A single occupant of a square, packed into one byte: type in the low 3 bits,
    /// colour in bit 3. <see cref="None"/> is the empty square.
    /// </summary>
    public readonly struct Piece : System.IEquatable<Piece>
    {
        const byte TypeMask = 0b0000_0111;
        const byte ColorBit = 0b0000_1000;

        public static readonly Piece None = new Piece(0);

        readonly byte value;

        Piece(byte value) => this.value = value;

        public Piece(PieceType type, PieceColor color)
        {
            value = (byte)((byte)type | (color == PieceColor.Black ? ColorBit : 0));
        }

        public PieceType Type => (PieceType)(value & TypeMask);
        public PieceColor Color => (value & ColorBit) != 0 ? PieceColor.Black : PieceColor.White;
        public bool IsNone => (value & TypeMask) == 0;
        public bool IsSome => (value & TypeMask) != 0;

        public bool Is(PieceType type, PieceColor color) => Type == type && Color == color;

        public bool Equals(Piece other) => value == other.value;
        public override bool Equals(object obj) => obj is Piece other && Equals(other);
        public override int GetHashCode() => value;
        public static bool operator ==(Piece a, Piece b) => a.value == b.value;
        public static bool operator !=(Piece a, Piece b) => a.value != b.value;

        /// <summary>Standard FEN letter: upper case for white, lower for black. '.' when empty.</summary>
        public char ToFenChar()
        {
            char c;
            switch (Type)
            {
                case PieceType.Pawn: c = 'p'; break;
                case PieceType.Knight: c = 'n'; break;
                case PieceType.Bishop: c = 'b'; break;
                case PieceType.Rook: c = 'r'; break;
                case PieceType.Queen: c = 'q'; break;
                case PieceType.King: c = 'k'; break;
                default: return '.';
            }
            return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
        }

        public static Piece FromFenChar(char c)
        {
            PieceColor color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
            switch (char.ToLowerInvariant(c))
            {
                case 'p': return new Piece(PieceType.Pawn, color);
                case 'n': return new Piece(PieceType.Knight, color);
                case 'b': return new Piece(PieceType.Bishop, color);
                case 'r': return new Piece(PieceType.Rook, color);
                case 'q': return new Piece(PieceType.Queen, color);
                case 'k': return new Piece(PieceType.King, color);
                default: return None;
            }
        }

        public override string ToString() => IsNone ? "-" : ToFenChar().ToString();
    }

    public static class ColorExtensions
    {
        public static PieceColor Opposite(this PieceColor color) =>
            color == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }
}
