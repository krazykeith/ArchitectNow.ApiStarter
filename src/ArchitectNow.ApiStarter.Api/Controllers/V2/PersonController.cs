﻿using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchitectNow.ApiStarter.Api.Models.ViewModels;
using ArchitectNow.ApiStarter.Api.Services;
using ArchitectNow.ApiStarter.Common.Models;
using ArchitectNow.ApiStarter.Common.Models.Exceptions;
using ArchitectNow.ApiStarter.Common.Models.ViewModels;
using ArchitectNow.ApiStarter.Common.Repositories;
using ArchitectNow.ApiStarter.Common.Services;
using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSwag.Annotations;

namespace ArchitectNow.ApiStarter.Api.Controllers.V2
{
    [ApiVersion("2.0")]
    public class PersonController : ApiV2BaseController
    {
        private readonly ICurrentUserService _currentUserService;
        private readonly IPersonRepository _personRepository;

        public PersonController(ICurrentUserService currentUserService,
            IMapper mapper,
            IServiceInvoker serviceInvoker,
            IPersonRepository personRepository) : base(mapper, serviceInvoker)
        {
            _currentUserService = currentUserService;
            _personRepository = personRepository;
        }

        /// <summary>
        ///     Secure method used to test security
        /// </summary>
        /// <returns></returns>
        [HttpGet("securitytest")]
        [SwaggerResponse(HttpStatusCode.OK, typeof(UserInformation))]
        [SwaggerResponse(HttpStatusCode.BadRequest, typeof(ApiError))]
        public async Task<IActionResult> SecurityTest()
        {
            return await ServiceInvoker.AsyncOk(() => _currentUserService.GetUserInformation());
        }

        /// <summary>
        ///     Search for people
        /// </summary>
        /// <param name="searchParams">Search parameters</param>
        /// <returns></returns>
        [HttpGet("search/{Id}")]
        [AllowAnonymous]
        [SwaggerResponse(HttpStatusCode.OK, typeof(List<PersonVm>))]
        public async Task<IActionResult> Search([FromQuery] string searchParams = "")
        {
            return await ServiceInvoker.AsyncOk(async () =>
            {
                var people = await _personRepository.Search(searchParams);

                return people.Select(x => Mapper.Map<PersonVm>(x));
            });
        }

        /// <summary>
        ///     Update person object
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        /// <exception cref="NotFoundException"></exception>
        [HttpPost("update")]
        [AllowAnonymous]
        [SwaggerResponse(HttpStatusCode.OK, typeof(PersonVm))]
        public async Task<IActionResult> Update([FromBody] PersonVm data)
        {
            return await ServiceInvoker.AsyncOk(async () =>
            {
                if (!data.Id.HasValue)
                {
                    var newItem = Mapper.Map<Person>(data);

                    newItem = await _personRepository.SaveAsync(newItem);

                    return Mapper.Map<PersonVm>(newItem);
                }

                var existingItem =
                    await _personRepository.GetOneAsync(data.Id.Value);

                if (existingItem == null)
                    throw new NotFoundException(nameof(Person), data.Id.Value);

                existingItem = Mapper.Map(data, existingItem);

                existingItem = await _personRepository.SaveAsync(existingItem);

                return Mapper.Map<PersonVm>(existingItem);
            });
        }
    }
}