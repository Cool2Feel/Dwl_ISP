namespace TimeUpdater.Services
{
    /// <summary>
    /// Calculates time values for device synchronization.
    /// Ported from the original MFC C++ timeUpdater project.
    /// Converts current local time to seconds since the year 2000.
    /// </summary>
    internal static class TimeCalculator
    {
        private const int YearBase = 2000;

        private static readonly int[] DaysInMonths = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };

        /// <summary>
        /// Determines whether the specified year is a leap year.
        /// </summary>
        private static bool IsLeapYear(int year)
        {
            bool leap = (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
            Logger.Info("[TimeCalculator] IsLeapYear({0}) = {1}", year, leap);
            return leap;
        }

        /// <summary>
        /// Converts the current local time to the total number of seconds since 2000-01-01 00:00:00.
        /// This matches the algorithm used in the original C++ application.
        /// </summary>
        public static uint GetSecondsSince2000()
        {
            DateTime now = DateTime.Now;
            Logger.Info("[TimeCalculator] GetSecondsSince2000: current local time = {0:yyyy-MM-dd HH:mm:ss}", now);
            uint result = GetSecondsSince2000(now);
            Logger.Info("[TimeCalculator] GetSecondsSince2000 result = {0} (0x{0:X8})", result);
            return result;
        }

        /// <summary>
        /// Converts the specified DateTime to the total number of seconds since 2000-01-01 00:00:00.
        /// </summary>
        public static uint GetSecondsSince2000(DateTime dateTime)
        {
            Logger.Info("[TimeCalculator] Computing seconds since {0}-01-01 00:00:00 for date {1:yyyy-MM-dd HH:mm:ss}",
                YearBase, dateTime);

            long totalDays = 0;

            // Accumulate days for each year from YearBase to the year before the current year
            for (int year = YearBase; year < dateTime.Year; year++)
            {
                totalDays += 365;
                if (IsLeapYear(year))
                {
                    totalDays += 1;
                    Logger.Info("[TimeCalculator] Year {0} is leap year, adding extra day.", year);
                }
            }

            Logger.Info("[TimeCalculator] Days from years (2000..{0}) = {1}", dateTime.Year - 1, totalDays);

            // Accumulate days for each month in the current year before the current month
            for (int month = 0; month < dateTime.Month - 1; month++)
            {
                totalDays += DaysInMonths[month];
                Logger.Info("[TimeCalculator]   Month {0} has {1} days, cumulative days = {2}",
                    month + 1, DaysInMonths[month], totalDays);
            }

            // Add February 29th if the current year is a leap year and the month is after February
            if (dateTime.Month > 2 && IsLeapYear(dateTime.Year))
            {
                totalDays += 1;
                Logger.Info("[TimeCalculator] Current year {0} is leap year and month > Feb, adding Feb 29.", dateTime.Year);
            }

            // Accumulate days in the current month (day - 1 because we count from day 0)
            long dayInMonth = dateTime.Day - 1;
            totalDays += dayInMonth;
            Logger.Info("[TimeCalculator] Days in current month (day-1) = {0}, total days = {1}", dayInMonth, totalDays);

            // Convert days to seconds
            long totalSeconds = totalDays * 24 * 60 * 60;

            // Add hours, minutes, and seconds
            long timeOfDaySeconds = dateTime.Hour * 60 * 60 + dateTime.Minute * 60 + dateTime.Second;
            totalSeconds += timeOfDaySeconds;

            Logger.Info("[TimeCalculator] Time of day in seconds = {0}, total seconds = {1}",
                timeOfDaySeconds, totalSeconds);

            Logger.Info("[TimeCalculator] Final result: {0} seconds since {1}-01-01 00:00:00",
                totalSeconds, YearBase);

            return (uint)totalSeconds;
        }
    }
}