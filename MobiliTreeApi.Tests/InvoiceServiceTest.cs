using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using MobiliTreeApi.Repositories;
using MobiliTreeApi.Services;
using Xunit;

namespace MobiliTreeApi.Tests
{
    public class InvoiceServiceTest
    {
        private readonly ISessionsRepository _sessionsRepository;
        private readonly IParkingFacilityRepository _parkingFacilityRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly ILogger<InvoiceService> _invoiceServiceLogger;

        public InvoiceServiceTest()
        {
            _sessionsRepository = new SessionsRepositoryFake(FakeData.GetSeedSessions());
            _parkingFacilityRepository = new ParkingFacilityRepositoryFake(FakeData.GetSeedServiceProfiles());
            _customerRepository = new CustomerRepositoryFake(FakeData.GetSeedCustomers());
            _invoiceServiceLogger = new Logger<InvoiceService>(new LoggerFactory());
        }

        [Fact]
        public void GivenSessionsService_WhenQueriedForInexistentParkingFacility_ThenThrowException()
        {
            var ex = Assert.Throws<ArgumentException>(() => GetSut().GetInvoices("nonExistingParkingFacilityId"));
            Assert.Equal("Invalid parking facility id 'nonExistingParkingFacilityId'", ex.Message);
        }

        [Fact]
        public void GivenEmptySessionsStore_WhenQueriedForUnknownParkingFacility_ThenReturnEmptyInvoiceList()
        {
            var result = GetSut().GetInvoices("pf001");

            Assert.Empty(result);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement()
        {
            var startDateTime = new DateTime(2018, 12, 15, 12, 25, 0);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });

            var result = GetSut().GetInvoices("pf001");
            
            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
        }

        [Fact]
        public void GivenMultipleSessionsInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnOneInvoicePerCustomer()
        {
            var startDateTime = new DateTime(2018, 12, 15, 12, 25, 0);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "c001",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "c001",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "c002",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });

            var result = GetSut().GetInvoices("pf001");

            Assert.Equal(2, result.Count);
            var invoiceCust1 = result.SingleOrDefault(x => x.CustomerId == "c001");
            var invoiceCust2 = result.SingleOrDefault(x => x.CustomerId == "c002");
            Assert.NotNull(invoiceCust1);
            Assert.NotNull(invoiceCust2);
            Assert.Equal("pf001", invoiceCust1.ParkingFacilityId);
            Assert.Equal("pf001", invoiceCust2.ParkingFacilityId);
            Assert.Equal("c001", invoiceCust1.CustomerId);
            Assert.Equal("c002", invoiceCust2.CustomerId);
        }

        [Fact]
        public void GivenMultipleSessionsForMultipleFacilitiesInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoicesOnlyForQueriedFacility()
        {
            var startDateTime = new DateTime(2018, 12, 15, 12, 25, 0);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "c001",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "c001",
                ParkingFacilityId = "pf002",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "c002",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });

            var result = GetSut().GetInvoices("pf001");

            Assert.Equal(2, result.Count);
            var invoiceCust1 = result.SingleOrDefault(x => x.CustomerId == "c001");
            var invoiceCust2 = result.SingleOrDefault(x => x.CustomerId == "c002");
            Assert.NotNull(invoiceCust1);
            Assert.NotNull(invoiceCust2);
            Assert.Equal("pf001", invoiceCust1.ParkingFacilityId);
            Assert.Equal("pf001", invoiceCust2.ParkingFacilityId);
            Assert.Equal("c001", invoiceCust1.CustomerId);
            Assert.Equal("c002", invoiceCust2.CustomerId);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_1h_1()
        {
            var startDateTime = new DateTime(2025, 04, 24, 0, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(0.5m, invoice.Amount);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_1h_2()
        {
            var startDateTime = new DateTime(2025, 04, 24, 10, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(2.5m, invoice.Amount);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_1h_3()
        {
            var startDateTime = new DateTime(2025, 04, 24, 20, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(1)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(1.5m, invoice.Amount);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_2h()
        {
            var startDateTime = new DateTime(2025, 04, 24, 6, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(2)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(3m, invoice.Amount);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_10h()
        {
            var startDateTime = new DateTime(2025, 04, 24, 0, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(10)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(11m, invoice.Amount);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_24h()
        {
            var startDateTime = new DateTime(2025, 04, 24, 0, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(24)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(40m, invoice.Amount);
        }

        [Fact]
        public void GivenOneSessionInTheStore_WhenQueriedForExistingParkingFacility_ThenReturnInvoiceListWithOneElement_CalculateCorrectValue_24h_wkd()
        {
            var startDateTime = new DateTime(2025, 04, 20, 0, 1, 0, DateTimeKind.Utc);
            _sessionsRepository.AddSession(new Domain.Session
            {
                CustomerId = "some customer",
                ParkingFacilityId = "pf001",
                StartDateTime = startDateTime,
                EndDateTime = startDateTime.AddHours(24)
            });

            var result = GetSut().GetInvoices("pf001");

            var invoice = Assert.Single(result);
            Assert.NotNull(invoice);
            Assert.Equal("pf001", invoice.ParkingFacilityId);
            Assert.Equal("some customer", invoice.CustomerId);
            Assert.Equal(47.2m, invoice.Amount);
        }

        private IInvoiceService GetSut()
        {
            return new InvoiceService(
                _sessionsRepository, 
                _parkingFacilityRepository,
                _customerRepository,
                _invoiceServiceLogger);
        }
    }
}
