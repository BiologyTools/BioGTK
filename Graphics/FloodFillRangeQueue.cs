using System;


namespace Bio.Graphics
{
    /// <summary>A queue of FloodFillRanges.</summary>
	public class FloodFillRangeQueue
	{
        FloodFillRange[] array;
        int size;
        int head;

        
        /* A property that returns the size of the queue. */
        public int Count
        {
            get { return size; }
        }

		/* A constructor that calls another constructor. */
        public FloodFillRangeQueue():this(10000)
		{

		}

        /* A constructor that takes an integer as an argument. */
        public FloodFillRangeQueue(int initialSize)
        {
            array = new FloodFillRange[initialSize];
            head = 0;
            size = 0;
        }

        /* Returning the first item in the queue. */
        public FloodFillRange First 
		{
			get { return array[head]; }
		}

        /// The queue is full if the head is at the end of the array
        /// 
        /// @param FloodFillRange A struct that contains the start and end of a range.
        public void Enqueue(ref FloodFillRange r) 
		{
			if (size+head == array.Length) 
			{
                FloodFillRange[] newArray = new FloodFillRange[2 * array.Length];
                Array.Copy(array, head, newArray, 0, size);
				array = newArray;
                head = 0;
			}
            array[head+(size++)] = r;
		}

        /// The function dequeues the first item in the queue and returns it
        /// 
        /// @return FloodFillRange
        public FloodFillRange Dequeue() 
		{
            FloodFillRange range = new FloodFillRange();
            if (size>0)
            {
                range = array[head];
                array[head] = new FloodFillRange();
                head++;//advance head position
                size--;//update size to exclude dequeued item
            }
            return range;
		}

		/*public void Clear() 
		{
			if (size > 0)
				Array.Clear(array, 0, size);
			size = 0;
		}*/

	}
}
