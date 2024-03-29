using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using api.Exceptions;
using api.Models.Request;
using api.Models.Respone;
using api.Services;
using api.utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.Controllers
{
    [Authorize(Policy = "access-token")]
    [ApiVersion("1.0")]
    [ApiController]
    [Route("api/v{version:apiVersion}/users/{userId}/vehicles")]
    public class VehiclesController : ControllerBase
    {
        private readonly IVehicleServices vehicleServices;
        private readonly IHttpContextAccessor httpContextAccessor;

        public VehiclesController(IVehicleServices vehicleServices, IHttpContextAccessor httpContextAccessor)
        {
            this.vehicleServices = vehicleServices;
            this.httpContextAccessor = httpContextAccessor;
        }


        [HttpGet]
        public IActionResult GetVehicles(int userId)
        {
            try
            {
                if (httpContextAccessor.HttpContext?.User == null)
                {
                    throw new TokenInvalidException("You are unauthorized");
                }
                string tokenUserid = httpContextAccessor.HttpContext.User.getUserID() ?? "";
                if (tokenUserid == "")
                {
                    throw new TokenInvalidException("The Token is invalid");
                }
                if (userId.ToString() != tokenUserid || httpContextAccessor.HttpContext.User.IsInRole("admin"))
                {
                    throw new TokenInvalidException("You are unauthorized, you can only access your own vehicles");
                }
                IEnumerable<VehicleResponseDto> vehicles = vehicleServices.getVehicles(userId);
                return Ok(vehicles);
            }
            catch (UserNotFoundException ex)
            {
                return NotFound(ex);
            }
            catch (TokenInvalidException ex)
            {
                return Unauthorized(ex);
            }
        }

        [HttpPost]
        public IActionResult AddVehicle(int userId, [FromBody] VehicleRequestDto vehicleRequestDto)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    throw new RequestInvalidException("Invalid Request Model");
                }
                if (httpContextAccessor.HttpContext?.User == null)
                {
                    throw new TokenInvalidException("You are unauthorized");
                }
                string tokenUserid = httpContextAccessor.HttpContext.User.getUserID() ?? "";
                if (tokenUserid == "")
                {
                    throw new TokenInvalidException("The Token is invalid");
                }
                if (userId.ToString() != tokenUserid || httpContextAccessor.HttpContext.User.IsInRole("admin"))
                {
                    throw new TokenInvalidException("You are unauthorized, you can only access your own vehicles");
                }
                bool result = vehicleServices.addVehicle(userId, vehicleRequestDto);
                return Created("", "Vehicle added successfully");
            }
            catch (UserNotFoundException ex)
            {
                return NotFound(ex);
            }
            catch (TokenInvalidException ex)
            {
                return Unauthorized(ex);
            }
            catch (vehicleAlreadyExistsException ex)
            {
                return Conflict(ex);
            }
            catch (InvalidVehicleTypeException ex)
            {
                return BadRequest(ex);
            }
            catch (InvalidVehicleLicenseException ex)
            {
                return BadRequest(ex);
            }
            catch (RequestInvalidException ex)
            {
                return BadRequest(ex);
            }
        }

        [HttpGet("{vehicleId}")]
        public IActionResult GetVehicle(int userId, int vehicleId)
        {
            try
            {
                if (httpContextAccessor.HttpContext?.User == null)
                {
                    throw new TokenInvalidException("You are unauthorized");
                }
                string userid = httpContextAccessor.HttpContext.User.getUserID() ?? "";
                if (userid == "")
                {
                    throw new TokenInvalidException("The Token is invalid");
                }
                if (userId.ToString() != userid || !httpContextAccessor.HttpContext.User.IsInRole("admin"))
                {
                    throw new TokenInvalidException("You are unauthorized");
                }
                VehicleResponseDto vehicle = vehicleServices.getVehicle(vehicleId, userId);
                return Ok(vehicle);
            }
            catch (UserNotFoundException ex)
            {
                return NotFound(ex);
            }
            catch (TokenInvalidException ex)
            {
                return Unauthorized(ex);
            }
            catch (vehicleNotFoundException ex)
            {
                return NotFound(ex);
            }

        }

        [HttpPatch("{vehicleId}")]
        public IActionResult UpdateVehicle(int userId, int vehicleId)
        {
            return Ok(userId);
        }

        [HttpDelete("{vehicleId}")]
        public IActionResult DeleteVehicle(int userId, int vehicleId)
        {
            return Ok(userId);
        }
    }


}