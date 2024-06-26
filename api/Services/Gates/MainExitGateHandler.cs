using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using api.Enums;
using api.Models;
using api.Models.Entity.NormalDB;
using api.Models.LprData;
using api.utils;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace api.Services.Gates
{
    public class MainExitGateHandler : GateHandler
    {
        public MainExitGateHandler(IServiceScopeFactory serviceScopeFactory) : base(serviceScopeFactory)
        {
        }

        public override async Task HandleGateEvent(LprReceiveModel lprReceiveModel)
        {
            using (var scope = serviceScopeFactory.CreateScope())
            {
                NormalDataBaseContext normalDataBaseContext = GetNormalDataBaseContext(scope); UserVehicles? vehicle = normalDataBaseContext.UserVehicles.FirstOrDefault(x => x.vehicleLicense == lprReceiveModel.vehicleLicense);
                ParkingLots? parkingLot = normalDataBaseContext.ParkingLots.FirstOrDefault(x => x.lotID == lprReceiveModel.lotID);
                parkingLot.avaiableRegularSpaces++;
                normalDataBaseContext.ParkingLots.Update(parkingLot);
                await normalDataBaseContext.SaveChangesAsync();

                await HandleFinalExit(lprReceiveModel, parkingLot, vehicle);
            }
        }

        private async Task HandleFinalExit(LprReceiveModel lprReceiveModel, ParkingLots parkingLot, UserVehicles? vehicles = null)
        {
            using (var scope = serviceScopeFactory.CreateScope())
            {
                NormalDataBaseContext normalDataBaseContext = GetNormalDataBaseContext(scope); ParkingRecords? parkingRecords = normalDataBaseContext.ParkingRecords.FirstOrDefault(x => x.vehicleLicense == lprReceiveModel.vehicleLicense && x.exitTime == null);

                if (parkingRecords == null)
                {
                    Console.WriteLine("No parking record found");
                    return;
                }

                parkingRecords.exitTime = TimeLoader.GetTime();
                normalDataBaseContext.ParkingRecords.Update(parkingRecords);
                await normalDataBaseContext.SaveChangesAsync();

                decimal price = await CalculateLastRecord(normalDataBaseContext, parkingLot, SpaceType.REGULAR, parkingRecords);

                Payments lastPayment = normalDataBaseContext.Payments.FirstOrDefault(x => x.paymentID == parkingRecords.paymentID);

                if (lastPayment.paymentStatus == PaymentStatus.Generated)
                {
                    lastPayment.amount = price;
                    // //Mark the paymnet is paid if it didn't make a pre payment before exit for demo purpose
                    // lastPayment.paymentStatus = price == 0 ? PaymentStatus.Completed : PaymentStatus.Completed;
                    // lastPayment.paymentTime = lastPayment.paymentStatus == PaymentStatus.Completed ? TimeLoader.GetTime() : TimeLoader.GetTime();
                    // lastPayment.paymentMethodType = price == 0 ? PaymentMethodType.Free : PaymentMethodType.Cash;
                    // lastPayment.paymentMethod = price == 0 ? PaymentMethod.Free : PaymentMethod.PaymentMachine;
                    // normalDataBaseContext.Payments.Update(lastPayment);
                    // await normalDataBaseContext.SaveChangesAsync();
                    lastPayment.paymentStatus = price == 0 ? PaymentStatus.Completed : PaymentStatus.Pending;
                    lastPayment.paymentTime = lastPayment.paymentStatus == PaymentStatus.Completed ? TimeLoader.GetTime() : null;
                    lastPayment.paymentMethodType = price == 0 ? PaymentMethodType.Free : null;
                    lastPayment.paymentMethod = price == 0 ? PaymentMethod.Free : null;
                    normalDataBaseContext.Payments.Update(lastPayment);
                    await normalDataBaseContext.SaveChangesAsync();
                }

                if (lastPayment.paymentStatus == PaymentStatus.Completed && lastPayment.paymentTime != null && lastPayment.paymentTime.Value.AddMinutes(GracePeriodForPayment) < TimeLoader.GetTime())
                {
                    //TODO: The vehicle has exceeded the grace period for payment, user will be charged for the extra hour
                    Console.WriteLine("The vehicle has exceeded the grace period for payment, user will be charged for the extra hour");
                    return;
                }

                IEnumerable<ParkingRecords> parkingRecordsList = await normalDataBaseContext.ParkingRecords.Where(x => x.vehicleLicense == lprReceiveModel.vehicleLicense && x.sessionID == parkingRecords.sessionID && x.lotID == parkingRecords.lotID)
                    .Include(x => x.payment)
                    .ToListAsync();



                decimal totalAmount = 0;
                decimal unPayedAmount = 0;
                bool isAllCompleted = true;
                foreach (ParkingRecords record in parkingRecordsList)
                {
                    if (record.payment.paymentStatus != PaymentStatus.Completed)
                    {
                        isAllCompleted = false;
                        unPayedAmount += record.payment.amount;
                    }
                    totalAmount += record.payment.amount;
                }
                Console.WriteLine(parkingRecords.sessionID);
                ParkingRecordSessions parkingRecordSessions = normalDataBaseContext.ParkingRecordSessions.FirstOrDefault(x => x.sessionID == parkingRecords.sessionID);
                Console.WriteLine(JsonConvert.SerializeObject(parkingRecordSessions));
                parkingRecordSessions.totalPrice = totalAmount;
                parkingRecordSessions.EndedAt = TimeLoader.GetTime();
                normalDataBaseContext.ParkingRecordSessions.Update(parkingRecordSessions);
                await normalDataBaseContext.SaveChangesAsync();

                if (isAllCompleted)
                {
                    Console.WriteLine("No payment required, finished parking session");
                    return;
                }

                Console.WriteLine("Total amount: " + totalAmount + " Unpayed amount: " + unPayedAmount);
            }
        }


        protected override async Task HandleReservation(LprReceiveModel lprReceiveModel, ParkingLots parkingLot, UserVehicles vehicles, Reservations reservations)
        {
            throw new NotImplementedException();
        }

        protected override async Task HandleWalkin(LprReceiveModel lprReceiveModel, ParkingLots parkingLot, UserVehicles? vehicles = null)
        {
            throw new NotImplementedException();
        }
    }
}