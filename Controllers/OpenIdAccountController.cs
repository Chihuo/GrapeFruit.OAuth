﻿using System.Web.Mvc;
using DotNetOpenAuth.Messaging;
using DotNetOpenAuth.OpenId.RelyingParty;
using NGM.OpenAuthentication.Core.OpenId;
using NGM.OpenAuthentication.Services;
using NGM.OpenAuthentication.ViewModels;
using Orchard;
using Orchard.ContentManagement;
using Orchard.Localization;
using Orchard.Security;
using Orchard.Themes;
using Orchard.Users.Models;

namespace NGM.OpenAuthentication.Controllers
{
    [Themed]
    public class OpenIdAccountController : Controller {
        private readonly IOpenIdRelyingPartyService _openIdRelyingPartyService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IOpenAuthenticationService _openAuthenticationService;
        private readonly IOrchardServices _orchardServices;
        private readonly IMembershipService _membershipService;

        public OpenIdAccountController(
            IOpenIdRelyingPartyService openIdRelyingPartyService, 
            IAuthenticationService authenticationService,
            IOpenAuthenticationService openAuthenticationService,
            IOrchardServices orchardServices,
            IMembershipService membershipService)
        {
            _openIdRelyingPartyService = openIdRelyingPartyService;
            _authenticationService = authenticationService;
            _openAuthenticationService = openAuthenticationService;
            _orchardServices = orchardServices;
            _membershipService = membershipService;
            T = NullLocalizer.Instance;
        }

        public Localizer T { get; set; }

        public ActionResult LogOn(string returnUrl) {
            if (_openIdRelyingPartyService.HasResponse) {
                // TODO : Not happy about this huge switch statement, consider a stratagy pattern possibly when I come to refactory?
                switch (_openIdRelyingPartyService.Response.Status) {
                    case AuthenticationStatus.Authenticated:
                        var userFound = _openAuthenticationService.GetUser(_openIdRelyingPartyService.Response.ClaimedIdentifier);

                        var userLoggedIn = _authenticationService.GetAuthenticatedUser();

                        if (userFound != null && userLoggedIn != null && userFound.Id.Equals(userLoggedIn.Id)) {
                            // The person is trying to log in as himself.. bit weird
                            return Redirect(!string.IsNullOrEmpty(returnUrl) ? returnUrl : "~/");
                        }
                        if (userFound != null && userLoggedIn != null && !userFound.Id.Equals(userLoggedIn.Id)) {
                            AddError("IdentifierAssigned", "ClaimedIdentifier has already been assigned to another account");
                            break;
                        }
                        if (userFound == null && userLoggedIn == null) {
                            // If I am not logged in, and I noone has this identifier, then go to register page to get them to confirm details.
                            
                            var registrationSettings = _orchardServices.WorkContext.CurrentSite.As<RegistrationSettingsPart>();

                            if ((registrationSettings != null) &&
                                (registrationSettings.UsersCanRegister == false)) {
                                AddError("AccessDenied", "User does not exist on system");
                                break;
                            }
                            else {
                                var registerModelBuilder = new RegisterModelBuilder(_openIdRelyingPartyService.Response, _membershipService);
                                var model = registerModelBuilder.Build();

                                TempData["registermodel"] = model;
                                
                                return RedirectToAction("Register", "Account", new {
                                    area = "Orchard.Users",
                                    claimedidentifier = model.ClaimedIdentifier,
                                    friendlyidentifier = model.FriendlyIdentifier
                                });
                            }
                        }

                        var user = userLoggedIn ?? userFound;

                        _openAuthenticationService.AssociateOpenIdWithUser(
                            user,
                            _openIdRelyingPartyService.Response.ClaimedIdentifier,
                            _openIdRelyingPartyService.Response.FriendlyIdentifierForDisplay);

                        _authenticationService.SignIn(user, false);

                        return Redirect(!string.IsNullOrEmpty(returnUrl) ? returnUrl : "~/");
                    case AuthenticationStatus.Canceled:
                        AddError("InvalidProvider", "Canceled at provider");
                        break;
                    case AuthenticationStatus.Failed:
                        AddError("UnknownError", _openIdRelyingPartyService.Response.Exception.Message);
                        break;
                }
            }

            return DefaultLogOnResult(returnUrl);
        }

        [HttpPost, ActionName("LogOn")]
        public ActionResult _LogOn(string returnUrl) {
            CreateViewModel viewModel = new CreateViewModel();
            TryUpdateModel(viewModel);

            return BuildLogOnAuthenticationRedirect(viewModel);
        }

        private ActionResult BuildLogOnAuthenticationRedirect(CreateViewModel viewModel) {
            var identifier = new OpenIdIdentifier(viewModel.OpenIdIdentifier);
            if (!identifier.IsValid) {
                AddError("OpenIdIdentifier", "Invalid Open ID identifier");
                return DefaultLogOnResult(viewModel.ReturnUrl);
            }

            try {
                var request = _openIdRelyingPartyService.CreateRequest(identifier);
                
                request.AddExtension(Claims.CreateClaimsRequest(_openAuthenticationService.GetSettings()));
                request.AddExtension(Claims.CreateFetchRequest(_openAuthenticationService.GetSettings()));

                return request.RedirectingResponse.AsActionResult();
            }
            catch (ProtocolException ex) {
                AddError("ProtocolException", string.Format("Unable to authenticate: {0}", ex.Message));
            }
            return DefaultLogOnResult(viewModel.ReturnUrl);
        }

        private void AddError(string key, string value) {
            var errorKey = string.Format("error-{0}", key);

            if (!TempData.ContainsKey(errorKey)) {
                TempData.Add(errorKey, value);
                ModelState.AddModelError(errorKey, value);
            } else {
                TempData[errorKey] = value;
            }
        }

        private ActionResult DefaultLogOnResult(string returnUrl) {
            return RedirectToAction("LogOn", "Account", new { area = "Orchard.Users", ReturnUrl = returnUrl });
        }
    }
}