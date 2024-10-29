using System;

namespace McSync.Utils
{
    public class GDriveServiceRetrier
    {
        public void RetryUntilThrowsNoException(Action retryableAction, Action<Exception, int> actionOnException)
        {
            bool isSuccessful = false;
            for (var numOfTrial = 1; numOfTrial <= 20 && !isSuccessful; numOfTrial++)
                try
                {
                    retryableAction();
                    isSuccessful = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    actionOnException(e, numOfTrial);
                }
        }
    }
}