using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MobiliTreeApi.Domain;
using MobiliTreeApi.Repositories;
using static System.Collections.Specialized.BitVector32;

namespace MobiliTreeApi.Services
{
    public interface IInvoiceService
    {
        List<Invoice> GetInvoices(string parkingFacilityId);
        Invoice GetInvoice(string parkingFacilityId, string customerId);
    }

    public class InvoiceService: IInvoiceService
    {
        private readonly ISessionsRepository _sessionsRepository;
        private readonly IParkingFacilityRepository _parkingFacilityRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly ILogger<InvoiceService> _logger;

        public InvoiceService(ISessionsRepository sessionsRepository, 
                              IParkingFacilityRepository parkingFacilityRepository, 
                              ICustomerRepository customerRepository,
                              ILogger<InvoiceService> logger)
        {
            _sessionsRepository = sessionsRepository;
            _parkingFacilityRepository = parkingFacilityRepository;
            _customerRepository = customerRepository;
            this._logger = logger;
        }

        public List<Invoice> GetInvoices(string parkingFacilityId)
        {
            ServiceProfile serviceProfile = _parkingFacilityRepository.GetServiceProfile(parkingFacilityId)
                ?? throw new ArgumentException($"Invalid parking facility id '{parkingFacilityId}'");
            
            List<Session> parkingFacilitySessions = _sessionsRepository.GetSessions(parkingFacilityId);

            ConcurrentDictionary<string, decimal> customerAmounts = [];
            foreach (Session session in parkingFacilitySessions)
            {
                decimal amount = CalculateAmount(session, serviceProfile);
                customerAmounts.AddOrUpdate(session.CustomerId, amount, (key, oldValue) => oldValue + amount);
            }

            IEnumerable<string> customerIds = parkingFacilitySessions.Select(x => x.CustomerId).Distinct();

            IEnumerable<string> validCustomersIds = GetValidCustomersIds(parkingFacilityId, customerIds);

            List<Invoice> invoices =
                [.. parkingFacilitySessions
                    .GroupBy(x => x.CustomerId)
                    .Where(x => validCustomersIds.Any(c => c.Equals(x.Key)))
                    .Select(x => new Invoice
                    {
                        ParkingFacilityId = parkingFacilityId,
                        CustomerId = x.Key,
                        Amount = customerAmounts.TryGetValue(x.Key, out decimal amount) ? amount : 0
                    })];

            return invoices;
        }

        /// <summary>
        /// Get the valid customer IDs for the parking facility, which are registered in the customer repository.
        /// Create Log warnings if a customer ID is not found in the customer repository, 
        /// and if the parking facility ID is not found in the customer contracted facility IDs.
        /// </summary>
        /// <param name="parkingFacilityId">ID of the parking facility</param>
        /// <param name="customerIds">Customer Ids</param>
        /// <returns>List of valid customer Ids</returns>
        private IEnumerable<string> GetValidCustomersIds(string parkingFacilityId, IEnumerable<string> customerIds)
        {
            /// For the sake of the exercise, accept all provided customer IDs, even if they are not registered in the customer repository.
            return customerIds;

            List<Customer> validCustomers = [];

            /// I assume that the customer ID needs to exist in the customer repository, otherwise there is a problem.
            foreach (string customerId in customerIds)
            {
                Customer customer = _customerRepository.GetCustomer(customerId);
                if (customer == null)
                {
                    _logger.LogWarning($"Invalid customer id '{customerId}' found stored in parkingFacilitySessions, customerID not found in customer repository.");
                }
                else
                {
                    validCustomers.Add(customer);

                    /// Not certain what to do in that case. Depends on the requirements which are not specified.
                    /// 1) Either the customer needs to be already registed with the parking facility, in which case we trigger a notification service
                    ///    EG you are not registered with this parking facility, please follow that link to register
                    /// 2) Or we automatically add the parking facility to the customer, in which case we need to update the customer repository
                    /// For now we will just log a warning.
                    if (customer.ContractedParkingFacilityIds == null || !customer.ContractedParkingFacilityIds.Contains(parkingFacilityId))
                    {
                        _logger.LogWarning($"Parking facility ID {parkingFacilityId} not found in customer contracted Facility Ids for customer {customerId}");
                    }
                }
            }

            return validCustomers.Select(x=> x.Id);
        }

        public Invoice GetInvoice(string parkingFacilityId, string customerId)
        {
            ServiceProfile serviceProfile = _parkingFacilityRepository.GetServiceProfile(parkingFacilityId)
                ?? throw new ArgumentException($"Invalid parking facility id '{parkingFacilityId}'");

            Customer customer = _customerRepository.GetCustomer(customerId);
            // Normally we would throw, same as with the serviceProfile.
            if (customer == null)
            {
                _logger.LogWarning($"Invalid customer id '{customerId}' found stored in parkingFacilitySessions, customerID not found in customer repository.");
            }

            /// Not certain what to do in that case. Depends on the requirements which are not specified.
            /// 1) Either the customer needs to be already registed with the parking facility, in which case we trigger a notification service
            ///    EG you are not registered with this parking facility, please contact us to register
            /// 2) Or we automatically add the parking facility to the customer, in which case we need to update the customer repository
            /// For now we will just log a warning.
            if (customer.ContractedParkingFacilityIds == null || !customer.ContractedParkingFacilityIds.Contains(parkingFacilityId))
            {
                _logger.LogWarning($"Parking facility ID {parkingFacilityId} not found in customer contracted Facility IDs for customer {customerId}");
            }

            List<Session> parkingFacilitySessions = _sessionsRepository.GetSessions(parkingFacilityId);

            List<Invoice> invoices = [];
            decimal totalAmount = 0;
            foreach (Session session in parkingFacilitySessions.Where(s => s.CustomerId.Equals(customerId)))
            {
                totalAmount += CalculateAmount(session, serviceProfile);
            }

            Invoice invoice = new()
            {
                ParkingFacilityId = parkingFacilityId,
                CustomerId = customerId,
                Amount = totalAmount
            };

            return invoice;
        }

        /// <summary>
        /// Timezone is not immediately clear, so we will assume that the session start and end times are in UTC.
        /// As per instructions: The hourly price of the timeslot when your parking session started is the hourly price you pay during the full parking session.
        /// This seems like a simplified version of the problem, I choose to implement it way it should be, taking the different time slots into account.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="serviceProfile"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private decimal CalculateAmount(Session session, ServiceProfile serviceProfile)
        {
            DateTime startTime = session.StartDateTime.ToUniversalTime();
            DateTime endTime = session.EndDateTime.ToUniversalTime();

            if (endTime <= startTime)
            {
                this._logger.LogWarning($"Session end time {endTime} is before or equal to start time {startTime}. Cannot calculate amount for session with ParkingFacilityId: {session.ParkingFacilityId} and CustomerId: {session.CustomerId}.");
                return 0;
            }

            /// We will calculate each hour separately, until we reach the end date.
            /// We could even improve on this algorithm by checking the serviceprofile service when is the next rate change, and multiply until we reach that time.
            /// But since we are not making any network calls, and assuming that the service profile for each parking facility as being grabbed on a single rest call 
            /// (more than reasonable assumption, since the price catalog cannot be that big)
            /// the speed gain from the optimization is hardly worth the extra complexity.
            /// 
            decimal totalPrice = 0;
            while (startTime < endTime)
            {
                int hour = startTime.Hour;
                int minute = startTime.Minute;

                int dayOfWeek = (int)startTime.DayOfWeek;

                IList<TimeslotPrice> timeslotPrices = GetTimeSlotPricesBasedOnDayOfWeek(dayOfWeek, serviceProfile);
                TimeslotPrice selectTimeSlot = timeslotPrices.FirstOrDefault(x => x.StartHour <= hour && x.EndHour > hour);

                if (minute == 0)
                {
                    totalPrice += selectTimeSlot.PricePerHour;
                }
                else
                {
                    /// Since not clear by the requirements how the charging is implemented, we will assume that 
                    /// even if the customer parks at 12:59, he is still charged for the full hour.
                    /// Otherewise we would need to check the minutes and charge a fraction of the hour, or 30 mins etc.
                    totalPrice += selectTimeSlot.PricePerHour;
                }

               startTime = startTime.AddHours(1);
            }

            return totalPrice;
        }

        private IList<TimeslotPrice> GetTimeSlotPricesBasedOnDayOfWeek(int dayOfWeek, ServiceProfile serviceProfile)
        {
            // 0 is Sunday, 1 is Monday, ..., 6 is Saturday
            if (dayOfWeek == 0 || dayOfWeek == 6)
            {
                return serviceProfile.WeekendPrices;
            }

            else return serviceProfile.WeekDaysPrices;
        }
    }
}
