﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Timetabling
{
    public class ScheduledClass
    {
        public ScheduledClass(DateTime timeStart, DateTime timeEnd)
        {
            TimeStart = timeStart;
            TimeEnd = timeEnd;
        }
        
        public Subject ParentSubject => ClassInfo.ParentSubject;
        public string ClassName => ClassInfo.ClassName;
        public string ClassDescription => ClassInfo.ClassName.Split(" ")[0];//eg, Lecture, Tutorial

        public string subjectDisplayName => ParentSubject.DisplayName;
        public string subjectShortCode => ParentSubject.ShortCode;
        
        public ClassInfo ClassInfo { get; set; }
        
        public string Location { get; set; }

        public DateTime TimeStart { get; set; }
        public DateTime TimeEnd { get; set; }
        
        public bool OnlyChoice
        {
            get
            {
                //If there's only one scheduled class in our parent class, then
                //we're the only choice
                return ClassInfo.ScheduledClasses.Count == 1;
            }
        }
            
        public short SlotStart { get { return dateToSlot(TimeStart); } }
        public short SlotEnd { get { return dateToSlot(TimeEnd); } }
        
        public TimeSpan Duration => TimeEnd - TimeStart;
        public short ClassNumber { get; set; }
        
        public bool DoTimetable { get; set; } = false;
        
        public List<ScheduledClass> ChildClasses { get; set; } = new List<ScheduledClass>();
        //Classes that are of the same number, e.g. stream lectures
        //These classes are dependent entirely upon the scheduling of this class.

        

        private static short dateToSlot(DateTime dt)
        {
            int day = (int)dt.DayOfWeek - 1;//Day indicating 0 for monday, 4 for friday.

            int hour = dt.Hour;//Hour between 0 and 23

            int quarter = dt.Minute / 15;//Quarter of hours between 0 and 3

            return (short)(day * 24 * 4 + hour * 4 + quarter);
        }
    }
}
