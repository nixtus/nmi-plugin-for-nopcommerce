using System.Xml.Serialization;

namespace Nixtus.Plugin.Payments.Nmi.Dtos
{
    [XmlRoot(ElementName = "customer")]
    public class Customer
    {
        [XmlElement(ElementName = "first_name")]
        public string FirstName { get; set; }
        [XmlElement(ElementName = "last_name")]
        public string LastName { get; set; }
        [XmlElement(ElementName = "address_1")]
        public string Address1 { get; set; }
        [XmlElement(ElementName = "address_2")]
        public string Address2 { get; set; }
        [XmlElement(ElementName = "company")]
        public string Company { get; set; }
        [XmlElement(ElementName = "city")]
        public string City { get; set; }
        [XmlElement(ElementName = "state")]
        public string State { get; set; }
        [XmlElement(ElementName = "postal_code")]
        public string PostalCode { get; set; }
        [XmlElement(ElementName = "country")]
        public string Country { get; set; }
        [XmlElement(ElementName = "email")]
        public string Email { get; set; }
        [XmlElement(ElementName = "phone")]
        public string Phone { get; set; }
        [XmlElement(ElementName = "fax")]
        public string Fax { get; set; }
        [XmlElement(ElementName = "cell_phone")]
        public string CellPhone { get; set; }
        [XmlElement(ElementName = "customertaxid")]
        public string CustomerTaxId { get; set; }
        [XmlElement(ElementName = "website")]
        public string Website { get; set; }
        [XmlElement(ElementName = "shipping_first_name")]
        public string ShippingFirstName { get; set; }
        [XmlElement(ElementName = "shipping_last_name")]
        public string ShippingLastName { get; set; }
        [XmlElement(ElementName = "shipping_address_1")]
        public string ShippingAddress1 { get; set; }
        [XmlElement(ElementName = "shipping_address_2")]
        public string ShippingAddress2 { get; set; }
        [XmlElement(ElementName = "shipping_company")]
        public string ShippingCompany { get; set; }
        [XmlElement(ElementName = "shipping_city")]
        public string ShippingCity { get; set; }
        [XmlElement(ElementName = "shipping_state")]
        public string ShippingState { get; set; }
        [XmlElement(ElementName = "shipping_postal_code")]
        public string ShippingPostalCode { get; set; }
        [XmlElement(ElementName = "shipping_country")]
        public string ShippingCountry { get; set; }
        [XmlElement(ElementName = "shipping_email")]
        public string ShippingEmail { get; set; }
        [XmlElement(ElementName = "shipping_carrier")]
        public string ShippingCarrier { get; set; }
        [XmlElement(ElementName = "tracking_number")]
        public string TrackingNumber { get; set; }
        [XmlElement(ElementName = "shipping_date")]
        public string ShippingDate { get; set; }
        [XmlElement(ElementName = "shipping")]
        public string Shipping { get; set; }
        [XmlElement(ElementName = "cc_number")]
        public string CcNumber { get; set; }
        [XmlElement(ElementName = "cc_hash")]
        public string CcHash { get; set; }
        [XmlElement(ElementName = "cc_exp")]
        public string CcExp { get; set; }
        [XmlElement(ElementName = "cc_start_date")]
        public string CcStartDate { get; set; }
        [XmlElement(ElementName = "cc_issue_number")]
        public string CcIssueNumber { get; set; }
        [XmlElement(ElementName = "check_account")]
        public string CheckAccount { get; set; }
        [XmlElement(ElementName = "check_hash")]
        public string CheckHash { get; set; }
        [XmlElement(ElementName = "check_aba")]
        public string CheckAba { get; set; }
        [XmlElement(ElementName = "check_name")]
        public string CheckName { get; set; }
        [XmlElement(ElementName = "account_holder_type")]
        public string AccountHolderType { get; set; }
        [XmlElement(ElementName = "account_type")]
        public string AccountType { get; set; }
        [XmlElement(ElementName = "sec_code")]
        public string SecCode { get; set; }
        [XmlElement(ElementName = "processor_id")]
        public string ProcessorId { get; set; }
        [XmlElement(ElementName = "cc_bin")]
        public string CcBin { get; set; }
        [XmlElement(ElementName = "created")]
        public string Created { get; set; }
        [XmlElement(ElementName = "updated")]
        public string Updated { get; set; }
        [XmlElement(ElementName = "account_updated")]
        public string AccountUpdated { get; set; }
        [XmlElement(ElementName = "customer_vault_id")]
        public string CustomerVaultId { get; set; }
        [XmlAttribute(AttributeName = "id")]
        public string Id { get; set; }
    }

    [XmlRoot(ElementName = "customer_vault")]
    public class CustomerVault
    {
        [XmlElement(ElementName = "customer")]
        public Customer Customer { get; set; }
    }

    [XmlRoot(ElementName = "nm_response")]
    public class NmiQueryResponse
    {
        [XmlElement(ElementName = "customer_vault")]
        public CustomerVault CustomerVault { get; set; }
    }
}
