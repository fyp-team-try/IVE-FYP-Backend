using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using api.Enums;
using api.Exceptions;
using api.Models;
using api.Models.Entity.NormalDB;
using api.Models.Request;
using api.Models.Respone;
using api.utils;
using Newtonsoft.Json;

namespace api.Services
{
    public interface IPaymentServices
    {
        IEnumerable<PaymentResponseDto> GetPaymentsByUserId(int userId);
        DetailedPaymentResponseDto GetPayment(int paymentID, int tokenUserID);
        bool MakePayment(int paymentID, MakePaymentRequestDto makePaymentRequestDto);
        decimal CalculatePrices(ParkingRecords parkingRecord, ParkingLots parkingLot, SpaceType spaceType, bool isReservated = false);
    }
    public class PaymentServices : IPaymentServices
    {
        private readonly NormalDataBaseContext normalDataBaseContext;

        public PaymentServices(NormalDataBaseContext normalDataBaseContext)
        {
            this.normalDataBaseContext = normalDataBaseContext;
        }

        public IEnumerable<PaymentResponseDto> GetPaymentsByUserId(int userId)
        {
            IEnumerable<Payments> payments = normalDataBaseContext.Payments.Where(p => p.userID == userId);
            return payments.Select(p => new PaymentResponseDto
            {
                paymentID = p.paymentID,
                userId = p.userID,
                amount = p.amount,
                paymentType = p.paymentType.ToString(),
                relatedID = p.relatedID,
                paymentMethod = p.paymentMethod.ToString(),
                paymentStatus = p.paymentStatus.ToString(),
                paymentIssuedAt = p.createdAt

            });
        }

        public DetailedPaymentResponseDto GetPayment(int paymentID, int tokenUserID)
        {
            Payments? payment = normalDataBaseContext.Payments.Find(paymentID);

            if (payment == null)
            {
                throw new PaymentNotFoundException("Payment not found");
            }

            if (payment.userID != tokenUserID)
            {
                throw new TokenInvalidException("Payment not found");
            }

            DetailedPaymentResponseDto response = new DetailedPaymentResponseDto
            {
                paymentID = payment.paymentID,
                userId = payment.userID,
                amount = payment.amount,
                paymentType = payment.paymentType.ToString(),
                relatedID = payment.relatedID,
                paymentMethod = payment.paymentMethod.ToString() ?? "",
                paymentStatus = payment.paymentStatus.ToString(),
                paymentIssuedAt = payment.createdAt
            };

            bool paymentTypeSuccess = Enum.TryParse<PaymentType>(payment.paymentType.ToString(), out PaymentType paymentType);

            //TODO: other payment types, for now only parking fee
            switch (paymentType)
            {
                case PaymentType.ParkingFee:
                    ParkingRecords? parkingRecord = normalDataBaseContext.ParkingRecords.Find(payment.relatedID);


                    ParkingLots? parkingLot = normalDataBaseContext.ParkingLots.Find(parkingRecord?.lotID);

                    if (parkingRecord == null)
                    {
                        throw new PaymentNotFoundException("Parking record not found");
                    }

                    if (parkingLot == null)
                    {
                        throw new PaymentNotFoundException("Parking lot not found");
                    }

                    response.parkingRecord = new ParkingRecordResponseDto
                    {
                        lotID = parkingRecord?.lotID ?? 0,
                        lotName = parkingLot.name,
                        spaceType = parkingRecord?.spaceType.ToString() ?? "",
                        entryTime = parkingRecord?.entryTime ?? DateTime.Now,
                        exitTime = parkingRecord?.exitTime,
                        vehicleLicense = parkingRecord?.vehicleLicense ?? ""
                    };

                    //not exit yet
                    if (payment.amount != -1)
                    {
                        return response;
                    }
                    bool spaceTypeSuccess = Enum.TryParse<SpaceType>(parkingRecord.spaceType.ToString(), out SpaceType spaceType);

                    Reservations? reservation = null;
                    if (parkingRecord.reservationID != null)
                    {
                        reservation = normalDataBaseContext.Reservations.Find(parkingRecord.reservationID);

                        if (reservation == null)
                        {
                            throw new PaymentNotFoundException("Reservation not found");
                        }
                        response.reservation = new ReservationResponseDto
                        {
                            reservationID = reservation.reservationID,
                            lotID = reservation.lotID,
                            lotName = parkingLot.name,
                            vehicleLicense = parkingRecord.vehicleLicense,
                            spaceType = parkingRecord.spaceType.ToString(),
                            startTime = reservation.startTime,
                            endTime = reservation.endTime,
                            price = reservation.price,
                            reservationStatus = reservation.reservationStatus,
                            createdTime = reservation.createdAt,
                            cancelledTime = reservation.canceledAt
                        };
                    }

                    decimal totalPrices = CalculatePrices(parkingRecord, parkingLot, spaceType, reservation != null);


                    Console.WriteLine(totalPrices);
                    response.amount = totalPrices;
                    break;
            }
            return response;
        }


        //simple success payment
        public bool MakePayment(int paymentID, MakePaymentRequestDto makePaymentRequestDto)
        {

            bool paymentMethodSuccess = Enum.TryParse<PaymentMethod>(makePaymentRequestDto.paymentMethod, out PaymentMethod paymentMethod);

            if (!paymentMethodSuccess)
            {
                throw new RequestInvalidException("Payment method must be: App, PaymentMachine");
            }

            bool paymentMethodTypeSuccess = Enum.TryParse<PaymentMethodType>(makePaymentRequestDto.paymentMethodType, out PaymentMethodType paymentMethodType);

            if (!paymentMethodTypeSuccess)
            {
                throw new RequestInvalidException("Payment method type must be: CreditCard, Cash, DebitCard, ApplePay, GooglePay, SamsungPay");
            }

            Payments? payment = normalDataBaseContext.Payments.Find(paymentID);

            if (payment == null)
            {
                throw new PaymentNotFoundException("Payment not found");
            }

            if (payment.paymentStatus != PaymentStatus.Pending)
            {
                throw new PaymentNotFoundException("Payment is Finished");
            }

            Reservations? reservation = null;
            ParkingRecords? parkingRecord = normalDataBaseContext.ParkingRecords.Find(payment.relatedID);

            if (parkingRecord == null)
            {
                throw new PaymentNotFoundException("Parking record not found");
            }

            ParkingLots? parkingLot = normalDataBaseContext.ParkingLots.Find(parkingRecord.lotID);

            if (parkingLot == null)
            {
                throw new PaymentNotFoundException("Parking lot not found");
            }

            SpaceType spaceType = parkingRecord.spaceType;

            if (parkingRecord.reservationID != null)
            {
                reservation = normalDataBaseContext.Reservations.Find(parkingRecord.reservationID);

                if (reservation == null)
                {
                    throw new PaymentNotFoundException("Reservation not found");
                }

            }

            decimal totalPrices = CalculatePrices(parkingRecord, parkingLot, spaceType, reservation != null);


            payment.amount = totalPrices;
            payment.paymentMethod = paymentMethod;
            payment.paymentMethodType = paymentMethodType;
            payment.paymentStatus = PaymentStatus.Completed;
            payment.paymentTime = DateTime.Now;

            normalDataBaseContext.SaveChanges();

            return true;
        }

        public decimal CalculatePrices(ParkingRecords parkingRecord, ParkingLots parkingLot, SpaceType spaceType, bool isReservated = false)
        {
            decimal? discount = null;
            if (isReservated)
            {
                discount = parkingLot.reservedDiscount;
            }
            decimal totalPrices = 0;
            switch (spaceType)
            {
                case SpaceType.ELECTRIC:
                    IEnumerable<LotPrices> electriclotPrices = JsonConvert.DeserializeObject<IEnumerable<LotPrices>>(parkingLot.electricSpacePrices);
                    totalPrices = PaymentUtils.CalculateParkingFee(electriclotPrices, parkingRecord.entryTime, parkingRecord.exitTime ?? DateTime.Now, discount);
                    break;
                default:
                    IEnumerable<LotPrices> regularlotPrices = JsonConvert.DeserializeObject<IEnumerable<LotPrices>>(parkingLot.regularSpacePrices);
                    totalPrices = PaymentUtils.CalculateParkingFee(regularlotPrices, parkingRecord.entryTime, parkingRecord.exitTime ?? DateTime.Now, discount);
                    break;
            }
            return totalPrices;
        }

        //TODO: get payment by parking session
        public Task GetPaymentByParkingSession(int userID, int sessionID)
        {
            throw new NotImplementedException();
        }
    }
}