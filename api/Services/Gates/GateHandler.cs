using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using api.Enums;
using api.Models;
using api.Models.Entity.NormalDB;
using api.Models.LprData;
using api.utils;
using Newtonsoft.Json;

namespace api.Services.Gates
{
    public abstract class GateHandler
    {
        protected readonly IServiceScopeFactory serviceScopeFactory;
        protected readonly int maxEarlyTime = -5;
        protected readonly int maxLateTime = 30;
        protected readonly int GracePeriodForPayment = 15;
        protected readonly int GracePeriodForFree = 30;
        protected GateHandler(IServiceScopeFactory serviceScopeFactory)
        {
            this.serviceScopeFactory = serviceScopeFactory;
        }
        protected NormalDataBaseContext GetNormalDataBaseContext(IServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
        }

        public abstract void HandleGateEvent(LprReceiveModel lprReceiveModel);

        protected abstract void HandleWalkin(NormalDataBaseContext normalDataBaseContext, LprReceiveModel lprReceiveModel, ParkingLots parkingLot, UserVehicles? vehicles = null);

        protected abstract void HandleReservation(NormalDataBaseContext normalDataBaseContext, LprReceiveModel lprReceiveModel, ParkingLots parkingLot, UserVehicles vehicles, Reservations reservations);

        protected async void CreatePaymentRecord(NormalDataBaseContext normalDataBaseContext, LprReceiveModel lprReceiveModel, int sessionID, SpaceType spaceType, UserVehicles? vehicles = null, Reservations? reservations = null)
        {
            Payments payment = new Payments
            {
                paymentType = PaymentType.ParkingFee,
                userID = vehicles == null ? 0 : vehicles.userID,
                amount = -1,
                paymentStatus = PaymentStatus.Generated,
            };

            normalDataBaseContext.Payments.Add(payment);
            await normalDataBaseContext.SaveChangesAsync();

            ParkingRecords parkingRecord = new ParkingRecords
            {
                lotID = lprReceiveModel.lotID,
                vehicleLicense = lprReceiveModel.vehicleLicense,
                spaceType = spaceType,
                entryTime = DateTime.Now,
                paymentID = payment.paymentID,
                sessionID = sessionID,
                reservationID = reservations == null ? null : reservations.reservationID,
            };

            normalDataBaseContext.ParkingRecords.Add(parkingRecord);
            await normalDataBaseContext.SaveChangesAsync();

            payment.relatedID = parkingRecord.parkingRecordID;
            await normalDataBaseContext.SaveChangesAsync();
        }

        protected int createSessionID(NormalDataBaseContext normalDataBaseContext, LprReceiveModel lprReceiveModel)
        {
            // int sessionID = normalDataBaseContext.ParkingRecords
            //     .Where(x => x.vehicleLicense == lprReceiveModel.vehicleLicense)
            //     .Max(parkingrecord => (int?)parkingrecord.sessionID) ?? 0;

            // sessionID++;

            ParkingRecordSessions parkingRecordSessions = new ParkingRecordSessions
            {
                vehicleLicense = lprReceiveModel.vehicleLicense,
                lotID = lprReceiveModel.lotID,
                CreatedAt = DateTime.Now,
            };

            normalDataBaseContext.ParkingRecordSessions.Add(parkingRecordSessions);
            normalDataBaseContext.SaveChanges();

            return parkingRecordSessions.sessionID;
        }

        protected Reservations? GetReservations(NormalDataBaseContext normalDataBaseContext, LprReceiveModel lprReceiveModel, UserVehicles vehicles, SpaceType spaceType)
        {
            return normalDataBaseContext.Reservations.FirstOrDefault(
                x => x.vehicleID == vehicles.vehicleID &&
                x.startTime.Date == DateTime.Now.Date &&
                x.endTime.Date == DateTime.Now.Date &&
                x.startTime.AddMinutes(maxEarlyTime) <= DateTime.Now &&
                x.startTime.AddMinutes(maxLateTime) <= DateTime.Now &&
                x.reservationStatus == ReservationStatus.PAID &&
                x.spaceType == spaceType &&
                x.lotID == lprReceiveModel.lotID
                );
        }



        //refacter this thank you lol, what 7 i doing
        protected decimal CalculateLastRecord(NormalDataBaseContext normalDataBaseContext, ParkingLots parkingLot, SpaceType spaceType, ParkingRecords parkingRecords)
        {
            if (parkingRecords.exitTime - parkingRecords.entryTime <= TimeSpan.FromMinutes(GracePeriodForFree))
            {
                return 0;
            }

            IEnumerable<LotPrices>? lotPrices = JsonConvert.DeserializeObject<IEnumerable<LotPrices>>(spaceType == SpaceType.REGULAR ? parkingLot.regularSpacePrices : parkingLot.electricSpacePrices);
            if (parkingRecords.reservationID == null)
            {
                return GetLastRecordPrices(lotPrices, parkingRecords);
            }
            else
            {
                Reservations? reservations = normalDataBaseContext.Reservations.FirstOrDefault(x => x.reservationID == parkingRecords.reservationID);
                reservations.reservationStatus = ReservationStatus.COMPLETED;
                normalDataBaseContext.SaveChanges();
                return GetLastRecordPrices(lotPrices, parkingRecords, reservations, parkingLot.reservedDiscount);
            }
        }
        protected decimal GetLastRecordPrices(IEnumerable<LotPrices> lotPrices, ParkingRecords parkingRecords, Reservations? reservation = null, decimal discount = 0)
        {
            decimal price = 0;
            int totalHours = (int)(DateTime.Now - parkingRecords.entryTime).TotalHours;
            int totalMinutes = (int)(DateTime.Now - parkingRecords.entryTime).TotalMinutes % 60;

            if (totalMinutes > GracePeriodForPayment)
            {
                totalHours++;
            }

            if (reservation == null)
            {
                price = CalculatePriceWithoutReservation(totalHours, lotPrices);
            }
            else if (DateTime.Now <= reservation.endTime.AddMinutes(GracePeriodForPayment))
            {
                price = CalculatePriceWithReservation(totalHours, lotPrices, discount);
            }
            else
            {
                int totalReservationHours = (int)(reservation.endTime - parkingRecords.entryTime).TotalHours;
                int totalHoursAfterReservation = totalHours - totalReservationHours;
                price = CalculatePriceWithReservation(totalReservationHours, lotPrices, discount);
                price += CalculatePriceWithoutReservation(totalHoursAfterReservation, lotPrices);
            }

            return price;
        }

        private decimal CalculatePriceWithoutReservation(int totalHours, IEnumerable<LotPrices> lotPrices)
        {
            decimal price = 0;

            for (int i = 0; i < totalHours; i++)
            {
                string timeString = (DateTime.Now - TimeSpan.FromHours(totalHours - i)).ToString("HH:00");
                decimal hourPrice = lotPrices.FirstOrDefault(lp => lp.time == timeString)?.price ?? 0;
                price += hourPrice;
            }

            return price;
        }

        private decimal CalculatePriceWithReservation(int totalHours, IEnumerable<LotPrices> lotPrices, decimal discount)
        {
            decimal price = CalculatePriceWithoutReservation(totalHours, lotPrices);

            return price * (1 - discount);
        }

        // no resevation: cal with the startTime, endTime
        // have resetvation and the REndtime is >= endTime: cal with startTime and endTime with discount
        // have restvation and the endTime is > RendTime: cal with the startTime to RendTime first, and RendTime to endtime
        // if the between exit and entry minutes is more than 15, add 1 hour
    }
}