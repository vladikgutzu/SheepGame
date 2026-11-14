using System;
using System.Collections.Generic;

namespace SheepGame.Core
{
    /// <summary>
    /// The colour rules, with no scene in them: carts arrive one colour at a time, a case of
    /// that colour boards the cart, any other case waits in a free place, and a case with no
    /// place left ends the round.
    /// </summary>
    // The places are a car park, not a line: when a cart of its colour pulls in, a waiting case
    // leaves from whichever place it is in. (A strict line was tried on 2026-09-14 and dropped
    // for the Bus Jam layout the user pointed at; the difficulty comes from how few places
    // there are and how far ahead the carts can be read.)
    //
    // Kept free of MonoBehaviour and of the board on purpose. The board decides which cases
    // can leave; this decides where a case goes once it has left. The two meet in exactly one
    // call, Arrive, which is also what lets the level tooling replay a board without a scene.
    //
    // Everything is decided the moment a case arrives rather than the moment it is tapped.
    // A tap only starts a case moving, and several can be moving at once; resolving them in
    // the order they reach the cart is the order the player sees, so the cart can never show
    // one thing while the rules believe another.
    public sealed class ColourMatchRules
    {
        public readonly struct Bag
        {
            public readonly int Id;
            public readonly int Colour;
            public Bag(int id, int colour) { Id = id; Colour = colour; }
        }

        readonly List<int> _carts;
        readonly List<Bag> _queue = new List<Bag>();
        readonly List<Bag> _overflow = new List<Bag>();

        int _cart;
        int _fill;

        public int CartCapacity { get; }
        public int QueueCapacity { get; private set; }

        public IReadOnlyList<int> Carts => _carts;
        public IReadOnlyList<Bag> Queue => _queue;
        public int CartIndex => _cart;
        public int CartFill => _fill;
        public bool IsComplete => _cart >= _carts.Count;
        public int CurrentColour => IsComplete ? -1 : _carts[_cart];
        public int QueueCount => _queue.Count;
        public bool QueueFull => _queue.Count >= QueueCapacity;
        public bool HasOverflow => _overflow.Count > 0;
        /// <summary>Cases that arrived to a full queue. More than one can: others are still on their way when the first does.</summary>
        public int OverflowCount => _overflow.Count;

        /// <summary>A case boarded cart <c>cart</c> in seat <c>seat</c>.</summary>
        public event Action<Bag, int, int> BoardedCart;
        /// <summary>A case took a place in the queue.</summary>
        public event Action<Bag, int> Queued;
        /// <summary>A waiting case left the queue for cart <c>cart</c>, seat <c>seat</c>.</summary>
        public event Action<Bag, int, int> LeftQueueForCart;
        /// <summary>The queue closed up; the list is the waiting cases, front first.</summary>
        public event Action<IReadOnlyList<Bag>> QueueChanged;
        /// <summary>Cart <c>cart</c> is full and goes.</summary>
        public event Action<int> CartFilled;
        /// <summary>A case arrived with nowhere to go. The round is lost.</summary>
        public event Action<Bag> Overflowed;
        /// <summary>The last cart has gone. The round is won.</summary>
        public event Action Completed;

        public ColourMatchRules(IList<int> carts, int cartCapacity, int queueCapacity)
        {
            _carts = new List<int>(carts);
            CartCapacity = Math.Max(1, cartCapacity);
            QueueCapacity = Math.Max(0, queueCapacity);
        }

        /// <summary>A case has reached the end of the board. Where it goes is decided here.</summary>
        public void Arrive(int id, int colour)
        {
            var bag = new Bag(id, colour);
            if (IsComplete)
            {
                // Can only happen on a level whose carts do not match its cases, which the
                // dealer refuses to build. Taking the case keeps a bad level finishable.
                return;
            }

            if (colour == _carts[_cart])
            {
                Board(bag);
                return;
            }

            if (_queue.Count < QueueCapacity)
            {
                _queue.Add(bag);
                Queued?.Invoke(bag, _queue.Count - 1);
                return;
            }

            _overflow.Add(bag);
            Overflowed?.Invoke(bag);
        }

        /// <summary>
        /// Widens the queue, which is what a continue buys. Cases that overflowed take the new
        /// places first, and anything that now matches the cart boards it.
        /// </summary>
        public void AddQueueSlots(int count)
        {
            if (count <= 0) return;
            QueueCapacity += count;

            while (_overflow.Count > 0 && _queue.Count < QueueCapacity)
            {
                var bag = _overflow[0];
                _overflow.RemoveAt(0);

                if (!IsComplete && bag.Colour == _carts[_cart]) Board(bag);
                else
                {
                    _queue.Add(bag);
                    Queued?.Invoke(bag, _queue.Count - 1);
                }
            }
        }

        void Board(Bag bag)
        {
            int seat = _fill++;
            BoardedCart?.Invoke(bag, _cart, seat);
            if (_fill >= CartCapacity) NextCart();
        }

        // A full cart goes, the next one pulls in, and whatever was waiting for it climbs
        // aboard before anything else can happen -- which can fill that one too, so it loops.
        void NextCart()
        {
            while (_fill >= CartCapacity)
            {
                CartFilled?.Invoke(_cart);
                _cart++;
                _fill = 0;

                if (IsComplete)
                {
                    Completed?.Invoke();
                    return;
                }

                int want = _carts[_cart];
                bool moved = false;
                for (int i = 0; i < _queue.Count && _fill < CartCapacity;)
                {
                    if (_queue[i].Colour != want) { i++; continue; }

                    var bag = _queue[i];
                    _queue.RemoveAt(i);
                    // Counted on a line of its own: inside ?.Invoke(...) the arguments are not
                    // evaluated at all when nobody is listening, and the seat would never fill.
                    int seat = _fill++;
                    LeftQueueForCart?.Invoke(bag, _cart, seat);
                    moved = true;
                }

                if (moved) QueueChanged?.Invoke(_queue);
            }
        }

        /// <summary>How many cases of each colour the carts still want, the current one included.</summary>
        public int Wanted(int colour)
        {
            int n = 0;
            for (int i = _cart; i < _carts.Count; i++)
                if (_carts[i] == colour) n += CartCapacity;
            if (!IsComplete && _carts[_cart] == colour) n -= _fill;
            return n;
        }
    }
}
