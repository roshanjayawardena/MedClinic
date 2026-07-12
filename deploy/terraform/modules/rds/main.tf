resource "aws_db_subnet_group" "main" {
  name       = "${var.name}-${var.environment}"
  subnet_ids = var.subnet_ids
}

resource "aws_security_group" "rds" {
  name   = "${var.name}-rds-${var.environment}"
  vpc_id = var.vpc_id

  ingress {
    from_port   = 5432
    to_port     = 5432
    protocol    = "tcp"
    cidr_blocks = var.allowed_cidr_blocks
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

resource "aws_db_instance" "postgres" {
  identifier              = "${var.name}-${var.environment}"
  engine                  = "postgres"
  engine_version          = "16"
  instance_class          = var.instance_class
  allocated_storage       = var.allocated_storage
  storage_encrypted       = true
  db_name                 = var.db_name
  username                = var.db_username
  password                = var.db_password
  db_subnet_group_name    = aws_db_subnet_group.main.name
  vpc_security_group_ids  = [aws_security_group.rds.id]
  backup_retention_period = 7
  deletion_protection     = true
  skip_final_snapshot     = false
  final_snapshot_identifier = "${var.name}-${var.environment}-final"
  tags = { Name = "${var.name}-${var.environment}" }
}

output "endpoint" { value = aws_db_instance.postgres.address }

variable "name"                 {}
variable "environment"          {}
variable "vpc_id"               {}
variable "subnet_ids"           { type = list(string) }
variable "allowed_cidr_blocks"  { type = list(string) }
variable "db_name"              { default = "mediclinic" }
variable "db_username"          {}
variable "db_password"          { sensitive = true }
variable "instance_class"       { default = "db.t4g.micro" }
variable "allocated_storage"    { default = 20 }
